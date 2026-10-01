// Adapted from OpenUsage v0.7.12 CodexLogFileParser.swift and CodexLogUsageScanner.swift.
// Copyright (c) 2026 Robin Ebers. MIT; see THIRD-PARTY-NOTICES.md.
using System.Text.Json;

namespace AIUsage.Core;

public sealed class ParserState
{
    public Tokens? Previous { get; set; }
    public string Model { get; set; } = "unknown";
    public string Tier { get; set; } = "standard";
    public bool SawMeta { get; set; }
    public bool ReplayGate { get; set; }
    public long? ChildCreated { get; set; }
    public string? SessionId { get; set; }
    public string? AccountKey { get; set; }
    public long Sequence { get; set; }
    public bool AccountingBlocked { get; set; }
    public bool AmbiguousReplay { get; set; }
    public ParserState Copy() => (ParserState)MemberwiseClone();
}

public sealed class CodexParser(ParserState? state = null)
{
    public ParserState State { get; } = state ?? new();
    public int Warnings { get; private set; }
    public UsageEvent? Parse(ReadOnlyMemory<byte> line, out QuotaSnapshot? quota)
    {
        quota = null;
        if (State.AccountingBlocked) return null;
        try
        {
            using var doc = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 64 });
            var root = doc.RootElement;
            string? type = root.Text("type");
            var p = root.Get("payload");
            if (type == "session_meta" && !State.SawMeta)
            {
                State.SawMeta = true;
                State.SessionId = Clean(p.Text("id"));
                State.AccountKey = AccountIdentity.Key(p.Text("creator_account_id"));
                State.ReplayGate = p.Get("forked_from_id").Present() || p.Get("parent_thread_id").Present() ||
                    p.Text("thread_source") == "subagent" || p.Get("source").Get("subagent").Present();
                if (State.ReplayGate) State.ChildCreated = root.Get("timestamp").Date()?.ToUnixTimeSeconds();
                return null;
            }
            if (type == "turn_context")
            {
                State.Model = Model(p) ?? State.Model;
                SetTier(p, completeSnapshot: false);
                return null;
            }
            if (type != "event_msg") return null;
            string? eventType = p.Text("type");
            if (eventType == "thread_settings_applied")
            {
                // Copied snapshots can identify their original owner, not the child rollout.
                string? owner = Clean(p.Text("thread_id"));
                if (owner is not null && State.SessionId is not null && owner != State.SessionId) return null;
                var snapshot = p.Get("thread_settings");
                if (snapshot.ValueKind == JsonValueKind.Object) State.Model = Model(snapshot) ?? State.Model;
                SetTier(p, completeSnapshot: snapshot.ValueKind == JsonValueKind.Object);
                return null;
            }
            if (eventType == "task_started")
            {
                var started = p.Get("started_at").Number();
                var gate = State.ChildCreated ?? root.Get("timestamp").Date()?.ToUnixTimeSeconds();
                if (State.ReplayGate && started.HasValue && gate.HasValue && started >= gate) State.ReplayGate = false;
                else if (State.ReplayGate && (!started.HasValue || !gate.HasValue) && !State.AmbiguousReplay)
                {
                    // Replayed log timestamps may be rewritten to the child's creation time.
                    // Using that timestamp to open this gate would silently charge parent history.
                    State.AmbiguousReplay = true;
                    Warnings++;
                }
                return null;
            }
            if (eventType != "token_count") return null;
            var at = root.Get("timestamp").Date();
            if (at is null) { Warnings++; return null; }
            var info = p.Get("info");
            var totalJson = info.Get("total_token_usage");
            Tokens? total = totalJson.ValueKind == JsonValueKind.Object ? Tokens.Read(totalJson) : null;
            if (State.ReplayGate) { if (total is not null) State.Previous = total; return null; }
            var rateLimits = p.Get("rate_limits");
            if (rateLimits.ValueKind == JsonValueKind.Object)
                quota = QuotaParser.Parse(rateLimits, at.Value, "Registro local") with { AccountKey = State.AccountKey };
            if (total is not null && total == State.Previous) return null;
            var last = info.Get("last_token_usage");
            Tokens? usage = last.ValueKind == JsonValueKind.Object ? Tokens.Read(last) : total?.Delta(State.Previous);
            if (last.ValueKind != JsonValueKind.Object && total is not null && State.Previous is not null &&
                (total.Input < State.Previous.Input || total.Output < State.Previous.Output)) Warnings++;
            if (total is not null) State.Previous = total;
            // set_total_tokens_full reports context occupancy, not consumed input/output tokens.
            if (usage is null || (usage.Input == 0 && usage.Cached == 0 && usage.Output == 0 && usage.Reasoning == 0)) return null;
            State.Model = Model(p) ?? Model(info) ?? State.Model;
            usage = usage with { Cached = Math.Min(usage.Cached, usage.Input), CacheWrite = Math.Min(usage.CacheWrite, Math.Max(0, usage.Input - usage.Cached)) };
            return new(at.Value, State.Model, usage, State.Tier, State.SessionId, total, total is null ? ++State.Sequence : 0);
        }
        catch (JsonException) { InvalidRecord(line.Span); return null; }
        catch (ArgumentException) { InvalidRecord(line.Span); return null; }
    }
    public void SkipOversized(ReadOnlySpan<byte> prefix)
    {
        if (RecordType(prefix) is "response_item" or "compacted") return;
        Warnings++;
        // Unknown/partial accounting or session metadata cannot safely seed subsequent deltas.
        State.AccountingBlocked = true;
    }
    private void InvalidRecord(ReadOnlySpan<byte> line)
    {
        Warnings++;
        if (!State.SawMeta && (RecordType(line) == "session_meta" || line.IndexOf("\"session_meta\""u8) >= 0))
            State.AccountingBlocked = true;
    }
    private static string? RecordType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith("\uFEFF"u8)) bytes = bytes[3..];
        try
        {
            var reader = new Utf8JsonReader(bytes, false, new JsonReaderState(new JsonReaderOptions { MaxDepth = 64 }));
            while (reader.Read())
                if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1 && reader.ValueTextEquals("type"))
                    return reader.Read() && reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        }
        catch (JsonException) { }
        return null;
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? Model(JsonElement p) => Clean(p.Text("model") ?? p.Text("model_name") ?? p.Get("metadata").Text("model"));
    private void SetTier(JsonElement p, bool completeSnapshot)
    {
        var tier = Clean(p.Get("thread_settings").Text("service_tier") ?? p.Text("service_tier"));
        if (tier is not null) State.Tier = tier.ToLowerInvariant();
        else if (completeSnapshot || p.Get("service_tier").ValueKind == JsonValueKind.Null) State.Tier = "standard";
    }
}

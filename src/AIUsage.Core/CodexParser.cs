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
}

public sealed class CodexParser(ParserState? state = null)
{
    public ParserState State { get; } = state ?? new();
    public int Warnings { get; private set; }
    public UsageEvent? Parse(ReadOnlyMemory<byte> line, out QuotaSnapshot? quota)
    {
        quota = null;
        try
        {
            using var doc = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 64 });
            var root = doc.RootElement;
            string? type = root.Text("type");
            var p = root.Get("payload");
            if (type == "session_meta" && !State.SawMeta)
            {
                State.SawMeta = true;
                State.ReplayGate = p.Get("forked_from_id").Present() || p.Get("parent_thread_id").Present() ||
                    p.Text("thread_source") == "subagent" || p.Get("source").Get("subagent").Present();
                if (State.ReplayGate) State.ChildCreated = root.Get("timestamp").Date()?.ToUnixTimeSeconds();
                return null;
            }
            if (type == "turn_context")
            {
                State.Model = Model(p) ?? State.Model;
                SetTier(p);
                return null;
            }
            if (type != "event_msg") return null;
            string? eventType = p.Text("type");
            if (eventType == "thread_settings_applied") { SetTier(p); return null; }
            if (eventType == "task_started")
            {
                var started = p.Get("started_at").Number();
                var gate = State.ChildCreated ?? root.Get("timestamp").Date()?.ToUnixTimeSeconds();
                if (State.ReplayGate && started.HasValue && gate.HasValue && started >= gate) State.ReplayGate = false;
                return null;
            }
            if (eventType != "token_count") return null;
            var at = root.Get("timestamp").Date();
            if (at is null) { Warnings++; return null; }
            var info = p.Get("info");
            var totalJson = info.Get("total_token_usage");
            Tokens? total = totalJson.ValueKind == JsonValueKind.Object ? Tokens.Read(totalJson) : null;
            // Seed the baseline from copied parent history, but never charge it to the child.
            if (State.ReplayGate) { if (total is not null) State.Previous = total; return null; }
            var rateLimits = p.Get("rate_limits");
            if (rateLimits.ValueKind == JsonValueKind.Object)
                quota = QuotaParser.Parse(rateLimits, at.Value, "Registro local");
            if (total is not null && total == State.Previous) return null;
            var last = info.Get("last_token_usage");
            Tokens? usage = last.ValueKind == JsonValueKind.Object ? Tokens.Read(last) : total?.Delta(State.Previous);
            // A decrease without last_token_usage is ambiguous: keep non-negative deltas and flag it.
            if (last.ValueKind != JsonValueKind.Object && total is not null && State.Previous is not null &&
                (total.Input < State.Previous.Input || total.Output < State.Previous.Output)) Warnings++;
            if (total is not null) State.Previous = total;
            if (usage is null || (usage.Input == 0 && usage.Output == 0 && usage.Total == 0)) return null;
            State.Model = Model(p) ?? Model(info) ?? State.Model;
            usage = usage with { Cached = Math.Min(usage.Cached, usage.Input), CacheWrite = Math.Min(usage.CacheWrite, Math.Max(0, usage.Input - usage.Cached)) };
            return new(at.Value, State.Model, usage, State.Tier);
        }
        catch (JsonException) { Warnings++; return null; }
        catch (ArgumentException) { Warnings++; return null; }
    }
    private static string? Model(JsonElement p)
    {
        var value = p.Text("model") ?? p.Text("model_name") ?? p.Get("metadata").Text("model");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
    private void SetTier(JsonElement p)
    {
        var tier = p.Get("thread_settings").Text("service_tier") ?? p.Text("service_tier");
        if (tier is not null) State.Tier = tier.Trim().ToLowerInvariant();
    }
}

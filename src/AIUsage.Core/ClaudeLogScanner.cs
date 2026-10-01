using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIUsage.Core;

/// One Claude Code API request. Message and request ids are kept only as digests, for deduplication.
public sealed record ClaudeEntry(string? Id, string? Request, bool Sidechain, bool HasSpeed, UsageEvent Event);

public sealed class ClaudeFileCache
{
    public int Schema { get; set; } // Missing/legacy versions must not deserialize as current.
    public long Size { get; set; }
    public long Modified { get; set; }
    public long Offset { get; set; }
    public string Prefix { get; set; } = "";
    public List<ClaudeEntry> Entries { get; set; } = [];
    public int Warnings { get; set; }
}

/// Reads token usage from the transcripts Claude Code writes on this PC: <config>\projects\**\*.jsonl.
/// Read-only and offline. It never opens credentials (.credentials.json), history.jsonl, settings or
/// anything outside projects\, never signs in and never contacts Anthropic. Only the usage counters,
/// model, speed and timestamp of assistant records are kept; prompts and responses are discarded.
public sealed class ClaudeLogScanner(string cacheDirectory)
{
    // Bump whenever parsing or deduplication semantics change.
    public const int SchemaVersion = 1;
    public const int MaxRecordBytes = 16 * 1024 * 1024;
    public const string UsInferenceSuffix = "@us";
    private readonly object sync = new();
    private readonly Dictionary<string, ClaudeFileCache> memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> strings = new(StringComparer.Ordinal);
    public int PersistentCacheHits { get; private set; }
    private static ClaudeFileCache EmptyCache() => new() { Schema = SchemaVersion };

    /// CLAUDE_CONFIG_DIR when set, otherwise %USERPROFILE%\.claude (Claude Code's own defaults).
    public static string ResolveHome()
    {
        string path = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? "";
        if (string.IsNullOrWhiteSpace(path)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        return AppSettings.ExpandFolder(path);
    }
    public ScanResult Scan(string home, CancellationToken ct = default)
    {
        lock (sync) { ct.ThrowIfCancellationRequested(); return ScanCore(home, ct); }
    }
    public void ClearCache()
    {
        lock (sync)
        {
            LocalPaths.Require(cacheDirectory);
            memory.Clear();
            if (Directory.Exists(cacheDirectory))
                foreach (var file in Directory.EnumerateFiles(cacheDirectory, "*.json", SearchOption.TopDirectoryOnly)) File.Delete(LocalPaths.Require(file));
        }
    }
    private ScanResult ScanCore(string home, CancellationToken ct)
    {
        home = LocalPaths.Require(home);
        LocalPaths.Require(cacheDirectory);
        var since = DateTimeOffset.Now.AddDays(-32);
        int warnings = 0, files = 0;
        var discovered = Discover(home, ref warnings, ct);
        var entries = new List<ClaudeEntry>();
        foreach (var path in discovered)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                LocalPaths.Require(path);
                var info = new FileInfo(path);
                if (info.LastWriteTimeUtc < since.UtcDateTime) continue;
                files++;
                var data = ReadFile(path, info, since, ct);
                entries.AddRange(data.Entries);
                warnings += data.Warnings;
            }
            catch (IOException) { warnings++; }
            catch (UnauthorizedAccessException) { warnings++; }
        }
        var active = discovered.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in memory.Keys.Where(k => !active.Contains(k)).ToArray()) memory.Remove(key);
        PruneCache();
        var events = Deduplicate(entries).Select(e => e.Event).Where(e => e.At >= since).OrderBy(e => e.At).ToList();
        return new(events, [], files, warnings, DateTimeOffset.Now);
    }
    /// Only *.jsonl under projects\ (subagent transcripts included), sorted so the first copy wins ties.
    internal static List<string> Discover(string home, ref int warnings, CancellationToken ct)
    {
        var result = new List<string>();
        string root;
        try { root = LocalPaths.Require(Path.Combine(home, "projects")); }
        catch (IOException) { warnings++; return result; }
        catch (UnauthorizedAccessException) { warnings++; return result; }
        if (!Directory.Exists(root)) return result;
        var stack = new Stack<string>(); stack.Push(root);
        while (stack.TryPop(out var dir))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                LocalPaths.Require(dir);
                foreach (var f in Directory.EnumerateFiles(dir, "*.jsonl"))
                {
                    try { LocalPaths.Require(f); result.Add(f); }
                    catch (IOException) { warnings++; }
                    catch (UnauthorizedAccessException) { warnings++; }
                }
                foreach (var d in Directory.EnumerateDirectories(dir))
                {
                    if (LocalPaths.IsResident(File.GetAttributes(d))) stack.Push(d);
                    else warnings++; // Do not follow junctions into loops or unrelated trees.
                }
            }
            catch (IOException) { warnings++; }
            catch (UnauthorizedAccessException) { warnings++; }
        }
        result.Sort(StringComparer.Ordinal);
        return result;
    }
    private ClaudeFileCache ReadFile(string path, FileInfo info, DateTimeOffset since, CancellationToken ct)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
        string cachePath = Path.Combine(cacheDirectory, key + ".json");
        if (!memory.TryGetValue(path, out var cache))
        {
            cache = Load(cachePath);
            if (cache is not null) { PersistentCacheHits++; Intern(cache.Entries); }
            cache ??= EmptyCache();
        }
        using var fs = new FileStream(LocalPaths.Require(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.SequentialScan);
        long size = fs.Length, modified = info.LastWriteTimeUtc.Ticks;
        byte[] head = new byte[(int)Math.Min(size, 512)];
        fs.ReadExactly(head);
        string prefix = Convert.ToHexString(SHA256.HashData(head));
        // Rewritten or truncated files are read again from the start; appended files continue at the checkpoint.
        if (cache.Schema != SchemaVersion || cache.Prefix != prefix || size < cache.Size || cache.Offset > size ||
            (size == cache.Size && cache.Modified != modified)) cache = EmptyCache();
        if (cache.Size == size && cache.Modified == modified && cache.Offset == size)
        {
            memory[path] = cache;
            return cache;
        }
        fs.Position = cache.Offset;
        using var record = new MemoryStream();
        byte[] buffer = new byte[65536];
        bool oversized = false;
        long position = fs.Position;
        while (position < size)
        {
            ct.ThrowIfCancellationRequested();
            int n = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, size - position));
            if (n == 0) break;
            for (int i = 0; i < n; i++)
            {
                position++;
                if (buffer[i] != (byte)'\n')
                {
                    if (record.Length < MaxRecordBytes && !oversized) record.WriteByte(buffer[i]);
                    else oversized = true;
                    continue;
                }
                if (oversized) cache.Warnings++;
                else ProcessRecord(record, cache, since);
                cache.Offset = position; // A trailing partial record is deliberately reread next time.
                record.SetLength(0);
                oversized = false;
            }
        }
        cache.Entries.RemoveAll(e => e.Event.At < since);
        cache.Size = size;
        cache.Modified = modified;
        cache.Prefix = prefix;
        memory[path] = cache;
        try { Directory.CreateDirectory(cacheDirectory); Save(cachePath, cache); }
        catch (IOException) { /* The cache only speeds up the next start. */ }
        catch (UnauthorizedAccessException) { }
        return cache;
    }
    private void ProcessRecord(MemoryStream record, ClaudeFileCache cache, DateTimeOffset since)
    {
        var bytes = record.GetBuffer().AsMemory(0, (int)record.Length);
        if (bytes.Span.StartsWith("﻿"u8)) bytes = bytes[3..];
        // Only assistant records carry usage; everything else (prompts, tool output) is skipped unparsed.
        if (bytes.Span.IndexOf("\"input_tokens\""u8) < 0) return;
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 256 });
            foreach (var entry in Parse(document.RootElement))
                if (entry.Event.At >= since) cache.Entries.Add(entry with { Event = entry.Event with { Model = Reuse(entry.Event.Model), Tier = Reuse(entry.Event.Tier) } });
        }
        catch (JsonException) { cache.Warnings++; }
    }
    /// The assistant record's own usage, plus one entry per advisor iteration (billed under its own model).
    /// Rules follow ccusage/OpenUsage so totals stay comparable with those tools.
    internal static List<ClaudeEntry> Parse(JsonElement root)
    {
        var result = new List<ClaudeEntry>();
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("timestamp", out var stamp) || stamp.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(stamp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)) return result;
        // Claude Code never writes null into these fields; a record that does has a foreign shape.
        if (HasNull(root, "cwd", "costUSD", "version", "sessionId", "requestId", "isApiErrorMessage") ||
            HasNull(message, "id", "model") || HasNull(usage, "speed", "cache_read_input_tokens", "cache_creation_input_tokens")) return result;
        if (root.TryGetProperty("version", out var version) && (version.ValueKind != JsonValueKind.String || !IsSemverPrefix(version.GetString()!))) return result;
        string? sessionId = Text(root, "sessionId"), requestId = Text(root, "requestId"), id = Text(message, "id"), model = Text(message, "model");
        if (sessionId == "" || requestId == "" || id == "" || model == "") return result;
        // "<synthetic>" marks messages Claude Code generated locally: no API request, nothing to bill.
        if (model is null || model == "<synthetic>") return result;
        bool usOnly = usage.TryGetProperty("inference_geo", out var geo) && geo.ValueKind == JsonValueKind.String &&
            string.Equals(geo.GetString(), "us", StringComparison.OrdinalIgnoreCase);
        if (!TryTokens(usage, usOnly, out var tokens, out var tier, out bool hasSpeed)) return result;
        bool sidechain = root.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True;
        string? digest = Digest(id), request = Digest(requestId);
        result.Add(new(digest, request, sidechain, hasSpeed, new UsageEvent(at, model, tokens, tier)));
        if (!usage.TryGetProperty("iterations", out var iterations) || iterations.ValueKind != JsonValueKind.Array) return result;
        int advisor = 0;
        foreach (var iteration in iterations.EnumerateArray())
        {
            if (iteration.ValueKind != JsonValueKind.Object || Text(iteration, "type") != "advisor_message" ||
                Text(iteration, "model") is not { Length: > 0 } advisorModel ||
                !TryTokens(iteration, usOnly, out var advisorTokens, out var advisorTier, out bool advisorSpeed)) continue;
            result.Add(new(id is null ? null : Digest(id + ":advisor:" + advisor.ToString(CultureInfo.InvariantCulture)), request,
                sidechain, advisorSpeed, new UsageEvent(at, advisorModel, advisorTokens, advisorTier)));
            advisor++;
        }
        return result;
    }
    /// Claude reports uncached input, cache reads and cache writes separately; AIUsage's Input is their sum.
    private static bool TryTokens(JsonElement usage, bool usOnly, out Tokens tokens, out string tier, out bool hasSpeed)
    {
        tokens = new(0, 0, 0, 0, 0); tier = "standard"; hasSpeed = false;
        if (!Count(usage, "input_tokens", true, out long input) || !Count(usage, "output_tokens", true, out long output) ||
            !Count(usage, "cache_read_input_tokens", false, out long read) || !Count(usage, "cache_creation_input_tokens", false, out long write5m)) return false;
        long write1h = 0;
        if (usage.TryGetProperty("speed", out var speed))
        {
            if (speed.ValueKind != JsonValueKind.String || speed.GetString() is not ("fast" or "standard")) return false;
            hasSpeed = true;
            if (speed.GetString() == "fast") tier = "fast";
        }
        // The 5m/1h split when present (1h writes cost more); otherwise the aggregate counts as 5m.
        if (usage.TryGetProperty("cache_creation", out var creation) && creation.ValueKind == JsonValueKind.Object)
        {
            if (!Count(creation, "ephemeral_5m_input_tokens", false, out write5m) || !Count(creation, "ephemeral_1h_input_tokens", false, out write1h)) return false;
        }
        if (usOnly) tier += UsInferenceSuffix;
        try
        {
            checked
            {
                long all = input + read + write5m + write1h;
                tokens = new(all, read, output, 0, all + output, write5m + write1h, write1h);
            }
        }
        catch (OverflowException) { return false; }
        return true;
    }
    private static bool Count(JsonElement e, string name, bool required, out long value)
    {
        value = 0;
        if (!e.TryGetProperty(name, out var p) || p.ValueKind == JsonValueKind.Null) return !required;
        return p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out value) && value >= 0;
    }
    private static bool HasNull(JsonElement e, params string[] names) =>
        names.Any(n => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Null);
    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    /// "digits.digits.digit…": accepts "2.1.0" and pre-releases, rejects foreign values such as "unknown".
    internal static bool IsSemverPrefix(string value)
    {
        int i = 0;
        bool Digits() { int start = i; while (i < value.Length && char.IsAsciiDigit(value[i])) i++; return i > start; }
        if (!Digits() || i >= value.Length || value[i++] != '.') return false;
        if (!Digits() || i >= value.Length || value[i++] != '.') return false;
        return i < value.Length && char.IsAsciiDigit(value[i]);
    }
    private static string? Digest(string? value) => value is null ? null :
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0, 16);
    /// Claude Code can log one request several times (resumed sessions, subagent sidechains). Keyed by
    /// (message id, request id); a sidechain copy under a new request id matches on the message id alone.
    /// On a collision prefer the main-chain record, then the larger total, then the record with a speed.
    internal static List<ClaudeEntry> Deduplicate(IEnumerable<ClaudeEntry> entries)
    {
        var result = new List<ClaudeEntry>();
        var exact = new Dictionary<string, int>(StringComparer.Ordinal);
        var byMessage = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry.Id is null) { result.Add(entry); continue; }
            string key = entry.Id + "|" + entry.Request;
            int hit = exact.TryGetValue(key, out int found) ? found : -1;
            if (hit < 0 && byMessage.TryGetValue(entry.Id, out var candidates))
                hit = candidates.FirstOrDefault(i => entry.Sidechain || result[i].Sidechain, -1);
            if (hit >= 0)
            {
                if (ShouldReplace(entry, result[hit]))
                {
                    exact.Remove(result[hit].Id + "|" + result[hit].Request);
                    result[hit] = entry; exact[key] = hit;
                }
                continue;
            }
            exact[key] = result.Count;
            if (!byMessage.TryGetValue(entry.Id, out var list)) byMessage[entry.Id] = list = [];
            list.Add(result.Count);
            result.Add(entry);
        }
        return result;
    }
    private static bool ShouldReplace(ClaudeEntry candidate, ClaudeEntry existing)
    {
        if (candidate.Sidechain != existing.Sidechain) return existing.Sidechain;
        if (candidate.Event.Tokens.Total != existing.Event.Tokens.Total) return candidate.Event.Tokens.Total > existing.Event.Tokens.Total;
        return candidate.HasSpeed && !existing.HasSpeed;
    }
    private string Reuse(string value)
    {
        if (strings.TryGetValue(value, out var known)) return known;
        if (strings.Count < 4096) strings.Add(value, value);
        return value;
    }
    private void Intern(List<ClaudeEntry> entries)
    {
        for (int i = 0; i < entries.Count; i++)
            entries[i] = entries[i] with { Event = entries[i].Event with { Model = Reuse(entries[i].Event.Model), Tier = Reuse(entries[i].Event.Tier) } };
    }
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };
    private static void Save(string path, ClaudeFileCache cache) =>
        AtomicJson.WriteStream(path, s => JsonSerializer.Serialize(s, cache, Compact), CacheStore.MaxFileBytes);
    private static ClaudeFileCache? Load(string path)
    {
        try
        {
            using var file = new FileStream(LocalPaths.Require(path), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (file.Length > CacheStore.MaxFileBytes) return null;
            var cache = JsonSerializer.Deserialize<ClaudeFileCache>(file, Compact);
            if (cache is not { Entries: not null, Offset: >= 0, Prefix: not null } || cache.Schema != SchemaVersion ||
                cache.Entries.Any(e => e?.Event?.Tokens is null || e.Event.Model is null || e.Event.Tier is null)) return null;
            return cache;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
    }
    private void PruneCache()
    {
        try
        {
            LocalPaths.Require(cacheDirectory);
            if (!Directory.Exists(cacheDirectory)) return;
            foreach (var f in Directory.EnumerateFiles(cacheDirectory, "*.json"))
                if (File.GetLastWriteTimeUtc(LocalPaths.Require(f)) < DateTime.UtcNow.AddDays(-35)) File.Delete(f);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Cache is optional. */ }
    }
}

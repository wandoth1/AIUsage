using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIUsage.Core;

/// One Devin model request; the message id is kept only as a digest, for deduplication.
public sealed record DevinEntry(string Id, long RowId, UsageEvent Event);

public sealed class DevinCache
{
    public int Schema { get; set; } // Missing/legacy versions must not deserialize as current.
    public long LastRowId { get; set; }
    public List<DevinEntry> Entries { get; set; } = [];
    public int Warnings { get; set; }
}

/// Reads token usage from the session database the Devin CLI / Devin Desktop agent keeps on this PC
/// (%APPDATA%\Devin\cli\sessions.db). Read-only and offline: the database is opened with SQLite's read-only
/// flag, only message_nodes.row_id, created_at and chat_message are queried, and from each message only its
/// role, id, model and token counters are used. Prompts, responses, titles, folders, tool calls, credentials
/// and every other Devin file are never read; AIUsage never signs in to or contacts Cognition.
public sealed class DevinLogScanner(string cacheDirectory)
{
    // Bump whenever parsing or deduplication semantics change.
    public const int SchemaVersion = 2;
    public const int MaxMessageBytes = 32 * 1024 * 1024;
    // Devin stores a message again when a conversation is compacted or branched; rows near the checkpoint are
    // reread so late copies and updates are seen. Identical copies are counted once.
    private const long Overlap = 2000;
    private readonly object sync = new();
    private DevinCache? memory;
    private byte[] buffer = new byte[256 * 1024];
    public int PersistentCacheHits { get; private set; }
    private string CachePath => Path.Combine(cacheDirectory, "devin.json");

    /// Where the Devin CLI keeps its sessions on Windows.
    public static string DefaultDatabase() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Devin", "cli", "sessions.db");

    public ScanResult Scan(string database, CancellationToken ct = default)
    {
        lock (sync) { ct.ThrowIfCancellationRequested(); return ScanCore(database, ct); }
    }
    public void ClearCache()
    {
        lock (sync)
        {
            LocalPaths.Require(cacheDirectory);
            memory = null;
            if (File.Exists(CachePath)) File.Delete(LocalPaths.Require(CachePath));
        }
    }
    private ScanResult ScanCore(string database, CancellationToken ct)
    {
        database = LocalPaths.Require(database);
        LocalPaths.Require(cacheDirectory);
        var since = DateTimeOffset.Now.AddDays(-32);
        if (!File.Exists(database)) return new([], [], 0, 0, DateTimeOffset.Now);
        if (memory is null)
        {
            memory = Load(CachePath);
            if (memory is not null) PersistentCacheHits++;
            memory ??= new DevinCache { Schema = SchemaVersion };
        }
        var cache = memory;
        long before = cache.LastRowId; int entriesBefore = cache.Entries.Count;
        // Without a write-ahead log nothing is writing: read the file as immutable, so SQLite creates no side files
        // in Devin's folder. With a log (Devin is running), read it through SQLite's normal shared read-only path.
        string wal = database + "-wal";
        bool immutable = !File.Exists(wal) || new FileInfo(wal).Length == 0;
        DateTime stamp = File.GetLastWriteTimeUtc(database);
        using (var sqlite = new WinSqlite(database, immutable))
        {
            sqlite.Prepare("select max(row_id) from message_nodes");
            long max = sqlite.Read() ? sqlite.Integer(0) : 0;
            if (max < cache.LastRowId) { cache = memory = new DevinCache { Schema = SchemaVersion }; before = -1; } // A different or reset database.
            var known = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < cache.Entries.Count; i++) known[cache.Entries[i].Id] = i;
            sqlite.Prepare("select row_id, created_at, chat_message from message_nodes where row_id > ?1 order by row_id");
            sqlite.Bind(1, Math.Max(0, cache.LastRowId - Overlap));
            while (sqlite.Read())
            {
                ct.ThrowIfCancellationRequested();
                long rowId = sqlite.Integer(0), created = sqlite.Integer(1);
                cache.LastRowId = Math.Max(cache.LastRowId, rowId);
                if (!sqlite.IsText(2)) continue;
                int length = sqlite.TextLength(2);
                if (length > MaxMessageBytes) { if (rowId > before) cache.Warnings++; continue; }
                if (length > buffer.Length) buffer = new byte[Math.Min(MaxMessageBytes, Math.Max(length, buffer.Length * 2))];
                length = sqlite.CopyText(2, buffer);
                var bytes = buffer.AsMemory(0, length);
                // Only model responses carry usage; user, tool and system messages are skipped unparsed.
                if (bytes.Span.IndexOf("\"generation_model\""u8) < 0) continue;
                DevinEntry? parsed;
                try { parsed = Parse(bytes, rowId, created); }
                catch (JsonException) { if (rowId > before) cache.Warnings++; continue; }
                if (parsed is not { } entry) continue;
                if (known.TryGetValue(entry.Id, out int index))
                {
                    if (entry.Event.Tokens.Total > cache.Entries[index].Event.Tokens.Total) cache.Entries[index] = entry with { RowId = cache.Entries[index].RowId };
                }
                else if (entry.Event.At >= since) { known[entry.Id] = cache.Entries.Count; cache.Entries.Add(entry); }
            }
        }
        // An immutable read assumed nobody was writing; if Devin started meanwhile, read again on the next refresh.
        if (immutable && (File.Exists(wal) && new FileInfo(wal).Length > 0 || File.GetLastWriteTimeUtc(database) != stamp))
        { memory = null; throw new IOException("The Devin database changed while it was being read."); }
        cache.Entries.RemoveAll(e => e.Event.At < since);
        if (cache.LastRowId != before || cache.Entries.Count != entriesBefore)
        {
            try { Directory.CreateDirectory(cacheDirectory); AtomicJson.WriteStream(CachePath, s => JsonSerializer.Serialize(s, cache, Compact), CacheStore.MaxFileBytes); }
            catch (IOException) { /* The cache only speeds up the next start. */ }
            catch (UnauthorizedAccessException) { }
        }
        var events = cache.Entries.Select(e => e.Event).Where(e => e.At >= since).OrderBy(e => e.At).ToList();
        return new(events, [], 1, cache.Warnings, DateTimeOffset.Now);
    }
    /// A model response with its usage, or null for anything else. Only metadata fields are read.
    internal static DevinEntry? Parse(ReadOnlyMemory<byte> message, long rowId, long createdSeconds)
    {
        using var document = JsonDocument.Parse(message, new JsonDocumentOptions { MaxDepth = 256 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || Text(root, "role") != "assistant" ||
            !root.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object ||
            !metadata.TryGetProperty("metrics", out var metrics) || metrics.ValueKind != JsonValueKind.Object ||
            Text(metadata, "generation_model") is not { Length: > 0 } generation) return null;
        if (!Count(metrics, "input_tokens", out long input) || !Count(metrics, "output_tokens", out long output) ||
            !Count(metrics, "cache_read_tokens", out long read) || !Count(metrics, "cache_creation_tokens", out long write)) return null;
        if (createdSeconds is <= 0 or > 253402300799) return null;
        Tokens tokens;
        // Devin reports uncached input, cache reads and cache writes separately; AIUsage's Input is their sum.
        try { checked { long all = input + read + write; tokens = new(all, read, output, 0, all + output, write); } }
        catch (OverflowException) { return null; }
        var (model, effort, tier) = SplitModel(generation);
        string identity = Text(root, "message_id") ?? Text(metadata, "request_id") ?? "row:" + rowId.ToString(CultureInfo.InvariantCulture);
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)), 0, 16);
        return new(id, rowId, new UsageEvent(DateTimeOffset.FromUnixTimeSeconds(createdSeconds), model, tokens, tier, Effort: effort));
    }
    /// Devin's model ids carry options as suffixes: "swe-2-high", "claude-opus-5-5-high-fast",
    /// "gpt-6-1-sol-medium-priority", "glm-5-2-max-1m". Split them into the model (as the price catalog names it),
    /// the reasoning effort (for the cost-by-effort breakdown) and the service tier (priority/fast cost more).
    public static (string Model, string? Effort, string Tier) SplitModel(string generation)
    {
        string model = generation.Trim().ToLowerInvariant();
        if (!IsModelName(model)) return ("unknown", null, "standard");
        var parts = model.Split('-').ToList();
        string? effort = null; string tier = "standard";
        while (parts.Count > 1)
        {
            string last = parts[^1];
            if (last is "1m" or "thinking") { parts.RemoveAt(parts.Count - 1); continue; }   // Same price as the base model.
            if (last is "priority" or "fast" && tier == "standard" && effort is null && parts.Count > 2 && IsEffort(parts[^2])) { tier = last; parts.RemoveAt(parts.Count - 1); continue; }
            if (effort is null && IsEffort(last)) { effort = last; parts.RemoveAt(parts.Count - 1); continue; }
            break;
        }
        // OpenAI writes versions with a dot ("gpt-6.1-sol"); Devin writes "gpt-6-1-sol".
        if (parts.Count >= 3 && parts[0] == "gpt" && parts[1].All(char.IsAsciiDigit) && parts[2].All(char.IsAsciiDigit) && parts[1].Length > 0 && parts[2].Length > 0)
        { parts[1] = parts[1] + "." + parts[2]; parts.RemoveAt(2); }
        return (string.Join('-', parts), effort, tier);
    }
    private static bool IsEffort(string value) => value is "minimal" or "none" or "low" or "medium" or "high" or "xhigh" or "max" or "ultra";
    private static bool IsModelName(string model) =>
        model.Length is > 0 and <= 80 && model.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.' or '_');
    private static bool Count(JsonElement e, string name, out long value)
    {
        value = 0;
        if (!e.TryGetProperty(name, out var p) || p.ValueKind == JsonValueKind.Null) return true;
        return p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out value) && value >= 0;
    }
    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };
    private static DevinCache? Load(string path)
    {
        try
        {
            if (!File.Exists(LocalPaths.Require(path))) return null;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (file.Length > CacheStore.MaxFileBytes) return null;
            var cache = JsonSerializer.Deserialize<DevinCache>(file, Compact);
            if (cache is not { Entries: not null } || cache.Schema != SchemaVersion || cache.LastRowId < 0 || cache.Warnings < 0 ||
                cache.Entries.Count > 1_000_000 || !cache.Entries.All(Valid)) return null;
            return cache;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
    }
    // A cache entry must be something Parse could have produced; anything else forces a rebuild from the database.
    private static bool Valid(DevinEntry? e)
    {
        if (e?.Event?.Tokens is not { } t || e.Id is not { Length: 32 } id || !id.All(char.IsAsciiHexDigit) || e.RowId < 0 ||
            e.Event.Model is null || !IsModelName(e.Event.Model) ||
            e.Event.Tier is not ("standard" or "priority" or "fast") || e.Event.SessionId is not null || e.Event.Cumulative is not null || e.Event.Sequence != 0 ||
            (e.Event.Effort is { } effort && EffortSummary.Normalize(effort) != effort) ||
            e.Event.At.Year < 2000 || e.Event.At > DateTimeOffset.Now.AddDays(1)) return false;
        if (t.Input < 0 || t.Cached < 0 || t.Output < 0 || t.Reasoning != 0 || t.CacheWrite < 0 || t.CacheWrite1h != 0 ||
            t.Cached > t.Input || t.CacheWrite > t.Input - t.Cached || t.Output > long.MaxValue - t.Input) return false;
        return t.Total == t.Input + t.Output;
    }
}

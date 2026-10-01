using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIUsage.Core;

public sealed class FileCache
{
    public int Schema { get; set; } // Missing/legacy versions must not deserialize as current.
    public long Size { get; set; }
    public long Modified { get; set; }
    public long Offset { get; set; }
    public string Prefix { get; set; } = "";
    public ParserState State { get; set; } = new();
    public List<UsageEvent> Events { get; set; } = [];
    public List<string>? EventPages { get; set; }
    public int StoredEventCount { get; set; }
    public List<QuotaSnapshot> Quotas { get; set; } = [];
    public int Warnings { get; set; }
    // An EOF record is a preview only. Offset/State remain BEFORE it so later appends can retract it.
    public UsageEvent? TailEvent { get; set; }
    public QuotaSnapshot? TailQuota { get; set; }
    public int TailWarnings { get; set; }
    public bool TailBlocked { get; set; }
}

/// Commits newline-terminated records; previews valid stable EOF records without advancing the checkpoint.
/// Cache stores token metadata only: no prompt, response, working-directory or credential content.
public sealed class LogScanner(string cacheDirectory)
{
    // Bump whenever parser, quota or deduplication semantics change.
    public const int ParserSchemaVersion = 5;
    private readonly object sync = new();
    public int PersistentCacheHits { get; private set; }
    private static FileCache EmptyCache() => new() { Schema = ParserSchemaVersion };
    public const int MaxRecordBytes = 2 * 1024 * 1024;
    private readonly Dictionary<string, FileCache> memory = new(StringComparer.OrdinalIgnoreCase);
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
        var events = new Dictionary<(string Scope, UsageEvent Event), UsageEvent>();
        var quotas = new List<QuotaSnapshot>();
        int warnings = 0, files = 0;
        var discovered = Discover(home, ref warnings, ct);
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
                if (!data.State.AccountingBlocked && !data.TailBlocked)
                {
                    string fallbackScope = Path.GetFullPath(path).ToUpperInvariant();
                    string? lastSession = null;
                    string lastScope = fallbackScope;
                    foreach (var e in data.Events.Concat(data.TailEvent is { } tail ? [tail] : []))
                    {
                        // Without a session id, two files cannot be proven to be copies.
                        if (lastSession != e.SessionId)
                        {
                            lastSession = e.SessionId;
                            lastScope = e.SessionId is null ? fallbackScope : $"{data.State.AccountKey}:{e.SessionId}";
                        }
                        string scope = lastScope;
                        if (e.At >= since) events.TryAdd((scope, e), e);
                    }
                    quotas.AddRange(data.Quotas);
                    if (data.TailQuota is not null) quotas.Add(data.TailQuota);
                }
                warnings += data.Warnings + data.TailWarnings;
            }
            catch (IOException) { warnings++; }
            catch (UnauthorizedAccessException) { warnings++; }
            catch (JsonException) { warnings++; }
        }
        // Drop entries for files removed/archived since the previous scan. Disk cache is pruned separately.
        var active = discovered.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in memory.Keys.Where(k => !active.Contains(k)).ToArray()) memory.Remove(key);
        PruneCache();
        return new(events.Values.OrderBy(e => e.At).ToList(), quotas, files, warnings, DateTimeOffset.Now);
    }
    private FileCache ReadFile(string path, FileInfo info, DateTimeOffset since, CancellationToken ct)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
        string cachePath = Path.Combine(cacheDirectory, key + ".json");
        if (!memory.TryGetValue(path, out var cache))
        {
            cache = Load(cachePath);
            if (cache is not null) PersistentCacheHits++;
            cache ??= EmptyCache();
        }
        using var fs = new FileStream(LocalPaths.Require(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.SequentialScan);
        long size = fs.Length, modified = info.LastWriteTimeUtc.Ticks;
        byte[] head = new byte[(int)Math.Min(size, 512)];
        fs.ReadExactly(head);
        string prefix = Convert.ToHexString(SHA256.HashData(head));
        if (cache.Schema != ParserSchemaVersion || cache.Prefix != prefix || size < cache.Size || cache.Offset > size ||
            (size == cache.Size && cache.Modified != modified)) cache = EmptyCache();
        bool stable = cache.Size == size && cache.Modified == modified && cache.Prefix == prefix;
        if (stable && cache.Offset == size)
        {
            memory[path] = cache;
            return cache;
        }
        cache.TailEvent = null; cache.TailQuota = null; cache.TailWarnings = 0; cache.TailBlocked = false;
        fs.Position = cache.Offset;
        var parser = new CodexParser(cache.State);
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
                ProcessRecord(record, oversized, parser, cache, since);
                cache.Offset = position; // A trailing partial record is deliberately reread next time.
                record.SetLength(0);
                oversized = false;
            }
        }
        if (stable && record.Length > 0)
        {
            var tailParser = new CodexParser(parser.State.Copy());
            var bytes = RecordBytes(record);
            if (oversized) tailParser.SkipOversized(bytes.Span);
            else
            {
                // Invalid EOF JSON may simply be an active writer's partial record: retry, don't warn.
                bool valid;
                try { using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 }); valid = true; }
                catch (JsonException) { valid = false; }
                QuotaSnapshot? q = null;
                if (valid) cache.TailEvent = tailParser.Parse(bytes, out q);
                cache.TailQuota = q;
            }
            cache.TailWarnings = tailParser.Warnings;
            cache.TailBlocked = tailParser.State.AccountingBlocked;
        }
        cache.State = parser.State;
        cache.Warnings += parser.Warnings;
        cache.Events.RemoveAll(e => e.At < since);
        cache.Size = size;
        cache.Modified = modified;
        cache.Prefix = prefix;
        memory[path] = cache;
        try { Directory.CreateDirectory(cacheDirectory); CacheStore.Save(cachePath, cache); }
        catch (IOException) { cache.Warnings++; }
        catch (UnauthorizedAccessException) { cache.Warnings++; }
        return cache;
    }
    private static ReadOnlyMemory<byte> RecordBytes(MemoryStream record)
    {
        var bytes = record.GetBuffer().AsMemory(0, (int)record.Length);
        return bytes.Span.StartsWith("\uFEFF"u8) ? bytes[3..] : bytes;
    }
    private static void ProcessRecord(MemoryStream record, bool oversized, CodexParser parser, FileCache cache, DateTimeOffset since)
    {
        var bytes = RecordBytes(record);
        if (oversized) parser.SkipOversized(bytes.Span);
        else if (!bytes.IsEmpty)
        {
            var span = bytes.Span;
            if (span.IndexOf("\"token_count\""u8) >= 0 || span.IndexOf("\"turn_context\""u8) >= 0 ||
                span.IndexOf("\"session_meta\""u8) >= 0 || span.IndexOf("\"task_started\""u8) >= 0 ||
                span.IndexOf("\"thread_settings_applied\""u8) >= 0)
            {
                var e = parser.Parse(bytes, out var quota);
                if (e is not null && e.At >= since) cache.Events.Add(e);
                if (quota is not null && quota.Windows.Count > 0)
                {
                    string identity = QuotaIdentity(quota);
                    cache.Quotas.RemoveAll(q => QuotaIdentity(q) == identity);
                    cache.Quotas.Add(quota);
                }
            }
        }
        if (parser.State.AccountingBlocked) { cache.Events.Clear(); cache.Quotas.Clear(); }
    }
    private static string QuotaIdentity(QuotaSnapshot q) => q.AccountKey + ":" +
        string.Join('|', q.Windows.Select(w => w.Id.Length > 0 ? w.Id : w.Name).Order(StringComparer.Ordinal));
    private static FileCache? Load(string path) => CacheStore.Load(path);
    // Shared with CodexFolder so folder detection counts exactly the files a scan would read.
    internal static List<string> Discover(string home, ref int warnings, CancellationToken ct)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<string>();
        foreach (string name in new[] { "sessions", "archived_sessions" })
        {
            try { string root = LocalPaths.Require(Path.Combine(home, name)); if (Directory.Exists(root)) roots.Add(root); }
            catch (IOException) { warnings++; }
            catch (UnauthorizedAccessException) { warnings++; }
        }
        bool directRolloutFolder = roots.Count == 0;
        if (directRolloutFolder) roots.Add(home);
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            var stack = new Stack<string>(); stack.Push(root);
            while (stack.TryPop(out var dir))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    LocalPaths.Require(dir);
                    foreach (var f in Directory.EnumerateFiles(dir, directRolloutFolder ? "rollout-*.jsonl" : "*.jsonl"))
                    {
                        if (Path.GetFileName(f).Equals("history.jsonl", StringComparison.OrdinalIgnoreCase)) continue;
                        try { LocalPaths.Require(f); }
                        catch (IOException) { warnings++; continue; }
                        catch (UnauthorizedAccessException) { warnings++; continue; }
                        string relative = Path.GetRelativePath(root, f);
                        if (seen.Add(relative)) result.Add(f); // Active copy wins over archived copy.
                    }
                    foreach (var d in Directory.EnumerateDirectories(dir))
                    {
                        if (LocalPaths.IsResident(File.GetAttributes(d))) stack.Push(d);
                        else warnings++; // Do not follow nested junctions into loops or unrelated trees.
                    }
                }
                catch (IOException) { warnings++; }
                catch (UnauthorizedAccessException) { warnings++; }
            }
        }
        return result;
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

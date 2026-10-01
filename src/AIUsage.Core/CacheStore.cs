using System.Text.Json;

namespace AIUsage.Core;

/// Each cache file uses the same read/write byte limit. Large event lists use immutable pages;
/// commit pages first and the manifest last so interruptions cannot produce partial totals.
public static class CacheStore
{
    public const long MaxFileBytes = 32 * 1024 * 1024;
    public const int PageEvents = 4096;
    private const int MaxPages = 4096;
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    public static void Save(string path, FileCache cache)
    {
        path = LocalPaths.Require(path);
        string directory = Path.GetDirectoryName(path)!;
        var previousPages = cache.EventPages;
        var pages = new List<string>();
        bool committed = false;
        try
        {
            if (cache.Events.Count > PageEvents)
            {
                if ((long)cache.Events.Count > (long)PageEvents * MaxPages) throw new IOException("Cache event limit exceeded.");
                string generation = Guid.NewGuid().ToString("N");
                for (int start = 0; start < cache.Events.Count; start += PageEvents)
                {
                    string name = Path.GetFileNameWithoutExtension(path) + ".p-" + generation + "-" + pages.Count + ".json";
                    pages.Add(name);
                    var entries = cache.Events.GetRange(start, Math.Min(PageEvents, cache.Events.Count - start));
                    AtomicJson.WriteStream(Path.Combine(directory, name), s => JsonSerializer.Serialize(s, entries, Compact), MaxFileBytes);
                }
            }
            var manifest = new FileCache
            {
                Schema = cache.Schema, Size = cache.Size, Modified = cache.Modified, Offset = cache.Offset,
                Prefix = cache.Prefix, State = cache.State, Quotas = cache.Quotas, Warnings = cache.Warnings,
                TailEvent = cache.TailEvent, TailQuota = cache.TailQuota, TailWarnings = cache.TailWarnings,
                TailBlocked = cache.TailBlocked, Events = pages.Count == 0 ? cache.Events : [],
                EventPages = pages.Count == 0 ? null : pages, StoredEventCount = cache.Events.Count
            };
            AtomicJson.WriteStream(path, s => JsonSerializer.Serialize(s, manifest, Compact), MaxFileBytes);
            committed = true;
            cache.EventPages = manifest.EventPages;
            cache.StoredEventCount = manifest.StoredEventCount;
        }
        finally
        {
            // Failed writes leave the previous manifest intact; successful writes retire only its pages.
            foreach (string name in (committed ? previousPages : pages) ?? [])
            {
                if (!ValidPageName(path, name)) continue;
                try { File.Delete(LocalPaths.Require(Path.Combine(directory, name))); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    public static FileCache? Load(string path)
    {
        try
        {
            var cache = Read<FileCache>(path);
            if (cache is not { State: not null, Events: not null, Quotas: not null, Offset: >= 0 } ||
                cache.Schema != LogScanner.ParserSchemaVersion || cache.StoredEventCount < 0) return null;
            if (cache.EventPages is { } pages)
            {
                if (pages.Count is 0 or > MaxPages || cache.Events.Count != 0 || pages.Distinct(StringComparer.Ordinal).Count() != pages.Count) return null;
                foreach (string name in pages)
                {
                    if (!ValidPageName(path, name)) return null;
                    var events = Read<List<UsageEvent>>(Path.Combine(Path.GetDirectoryName(path)!, name));
                    if (events is null || events.Count is 0 or > PageEvents) return null;
                    cache.Events.AddRange(events);
                }
            }
            if (cache.StoredEventCount != cache.Events.Count || cache.Events.Any(e => e is null || e.Tokens is null || e.Model is null || e.Tier is null)) return null;
            // Reuse repeated model/tier/session strings instead of retaining a string per deserialized event.
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            string? Reuse(string? value)
            {
                if (value is null) return null;
                if (strings.TryGetValue(value, out var known)) return known;
                if (strings.Count < 65536) strings.Add(value, value);
                return value;
            }
            for (int i = 0; i < cache.Events.Count; i++)
            {
                var e = cache.Events[i];
                cache.Events[i] = e with { Model = Reuse(e.Model)!, Tier = Reuse(e.Tier)!, SessionId = Reuse(e.SessionId) };
            }
            return cache;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
    }

    private static T? Read<T>(string path)
    {
        using var file = new FileStream(LocalPaths.Require(path), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (file.Length > MaxFileBytes) throw new IOException("Cache file exceeds its read/write limit.");
        return JsonSerializer.Deserialize<T>(file, Compact);
    }
    private static bool ValidPageName(string manifest, string name)
    {
        string prefix = Path.GetFileNameWithoutExtension(manifest) + ".p-";
        if (string.IsNullOrEmpty(name) || !name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.Ordinal)) return false;
        string suffix = name[prefix.Length..^5];
        int dash = suffix.IndexOf('-');
        return dash == 32 && suffix.Length > 33 && suffix.Length <= 40 &&
            suffix[..32].All(char.IsAsciiHexDigit) && suffix[33..].All(char.IsAsciiDigit);
    }
}

/// Bounds streamed serialization without constructing a potentially enormous UTF-16 JSON string.
internal sealed class SizeLimitedWriteStream(Stream target, long maximum) : Stream
{
    private long written;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => written;
    public override long Position { get => written; set => throw new NotSupportedException(); }
    public override void Flush() => target.Flush();
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length > maximum - written) throw new IOException("Cache file exceeds its read/write limit.");
        target.Write(buffer); written += buffer.Length;
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

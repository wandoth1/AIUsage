using static AIUsage.Core.L10n;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIUsage.Core;

public static class J
{
    public static JsonElement Get(this JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) ? v : default;
    public static string? Text(this JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    public static string? Text(this JsonElement e, string key) => e.Get(key).Text();
    public static double? Number(this JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var n) && double.IsFinite(n)) return n;
        if (e.ValueKind == JsonValueKind.String && double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out n) && double.IsFinite(n)) return n;
        return null;
    }
    public static long Count(this JsonElement e, params string[] keys)
    {
        foreach (var key in keys)
        {
            var n = e.Get(key).Number();
            if (n.HasValue) return (long)Math.Clamp(n.Value, 0, 1_000_000_000_000_000d);
        }
        return 0;
    }
    public static bool Present(this JsonElement e) => e.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) &&
        (e.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(e.GetString()));
    public static DateTimeOffset? Date(this JsonElement e)
    {
        if (DateTimeOffset.TryParse(e.Text(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)) return d;
        var n = e.Number();
        if (n is >= -62135596800 and <= 253402300799) return DateTimeOffset.FromUnixTimeSeconds((long)n.Value);
        return null;
    }
}

// Output already includes reasoning tokens. Cached input is a subset of input, not extra input.
// CacheWrite is also a subset of input; CacheWrite1h is the part of CacheWrite kept for one hour (Claude).
public sealed record Tokens(long Input, long Cached, long Output, long Reasoning, long Total, long CacheWrite = 0, long CacheWrite1h = 0)
{
    public static Tokens Read(JsonElement e)
    {
        long input = e.Count("input_tokens", "prompt_tokens", "input");
        long output = e.Count("output_tokens", "completion_tokens", "output");
        long cached = Math.Min(input, e.Count("cached_input_tokens", "cache_read_input_tokens", "cached_tokens"));
        long write = Math.Min(input - cached, e.Count("cache_creation_input_tokens", "cache_write_input_tokens"));
        long total = e.Count("total_tokens");
        return new(input, cached, output, e.Count("reasoning_output_tokens", "reasoning_tokens"), total > 0 ? total : input + output, write);
    }
    public Tokens Delta(Tokens? previous)
    {
        if (previous is null) return this;
        return new(Math.Max(0, Input - previous.Input), Math.Max(0, Cached - previous.Cached),
            Math.Max(0, Output - previous.Output), Math.Max(0, Reasoning - previous.Reasoning),
            Math.Max(0, Total - previous.Total), Math.Max(0, CacheWrite - previous.CacheWrite),
            Math.Max(0, CacheWrite1h - previous.CacheWrite1h));
    }
}

// Effort is the reasoning effort the tool recorded for the request (low … max). It changes how many tokens a
// request uses, never the per-token price.
public sealed record UsageEvent(DateTimeOffset At, string Model, Tokens Tokens, string Tier = "standard",
    string? SessionId = null, Tokens? Cumulative = null, long Sequence = 0, string? Effort = null);
public sealed record LimitWindow(string Name, double UsedPercent, DateTimeOffset? ResetAt, long? Seconds, string Id = "");
public sealed record QuotaSnapshot(DateTimeOffset At, string Source, string? Plan, List<LimitWindow> Windows,
    string? Credits = null, long? ResetCredits = null, string? AccountKey = null);
public sealed record ScanResult(List<UsageEvent> Events, List<QuotaSnapshot> Quotas, int Files, int Warnings, DateTimeOffset At);
public sealed record ModelSummary(string Model, long Input, long Cached, long Output, long Total, decimal KnownCost, int Unpriced, int Events, int Qualified = 0, string PricingNotes = "",
    List<EffortSummary>? Efforts = null)
{
    public List<EffortSummary> EffortRows => Efforts ?? [];
    // Value equality, including the per-effort rows (a list would otherwise compare by reference).
    public bool Equals(ModelSummary? other) => other is not null && Model == other.Model && Input == other.Input && Cached == other.Cached &&
        Output == other.Output && Total == other.Total && KnownCost == other.KnownCost && Unpriced == other.Unpriced && Events == other.Events &&
        Qualified == other.Qualified && PricingNotes == other.PricingNotes && EffortRows.SequenceEqual(other.EffortRows);
    public override int GetHashCode() => Model.GetHashCode() ^ Total.GetHashCode() ^ (Events * 31) ^ EffortRows.Count;
}
/// Usage of one model at one effort level; Effort is null when the log did not record it.
public sealed record EffortSummary(string? Effort, long Total, decimal KnownCost, int Unpriced, int Events)
{
    private static readonly string[] Order = ["max", "ultra", "xhigh", "high", "medium", "low", "minimal", "none"];
    public static string? Normalize(string? value)
    {
        string? effort = value?.Trim().ToLowerInvariant();
        return effort is { Length: > 0 and <= 16 } && effort.All(c => c is (>= 'a' and <= 'z') or '_') ? effort : null;
    }
    /// Highest effort first, unknown levels after known ones, missing effort last.
    public static int Rank(string? effort) => effort is null ? int.MaxValue : Array.IndexOf(Order, effort) is >= 0 and var i ? i : Order.Length;
    public static List<EffortSummary> Merge(IEnumerable<EffortSummary> rows) => rows
        .GroupBy(r => r.Effort ?? "", StringComparer.Ordinal)
        .Select(g => new EffortSummary(g.First().Effort, g.Sum(r => r.Total), g.Sum(r => r.KnownCost), g.Sum(r => r.Unpriced), g.Sum(r => r.Events)))
        .OrderBy(r => Rank(r.Effort)).ThenBy(r => r.Effort, StringComparer.Ordinal).ToList();
}

public static class UsageSummary
{
    public static DateOnly Day(DateTimeOffset at, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);
    public static List<ModelSummary> Group(IEnumerable<UsageEvent> source, PriceCatalog prices)
    {
        // Avoid retaining a PriceQuote array for every event (large histories can contain millions).
        var rows = new Dictionary<string, SummaryBuilder>(StringComparer.Ordinal);
        foreach (var e in source)
        {
            if (!rows.TryGetValue(e.Model, out var row)) rows[e.Model] = row = new();
            var quote = prices.Quote(e);
            checked
            {
                row.Input += e.Tokens.Input; row.Cached += e.Tokens.Cached; row.Output += e.Tokens.Output;
                row.Total += e.Tokens.Total; row.Cost += quote.Cost ?? 0; row.Events++;
            }
            if (quote.Cost is null) row.Unpriced++;
            if (quote.Note.Length > 0) { row.Qualified++; row.Notes.Add(quote.Note); }
            string effort = e.Effort ?? "";
            if (!row.Efforts.TryGetValue(effort, out var level)) row.Efforts[effort] = level = new();
            checked { level.Total += e.Tokens.Total; level.Cost += quote.Cost ?? 0; level.Events++; }
            if (quote.Cost is null) level.Unpriced++;
        }
        return rows.Select(x => new ModelSummary(x.Key, x.Value.Input, x.Value.Cached, x.Value.Output,
            x.Value.Total, x.Value.Cost, x.Value.Unpriced, x.Value.Events, x.Value.Qualified, string.Join(" | ", x.Value.Notes),
            EffortSummary.Merge(x.Value.Efforts.Select(l => new EffortSummary(l.Key.Length == 0 ? null : l.Key, l.Value.Total, l.Value.Cost, l.Value.Unpriced, l.Value.Events)))))
            .OrderByDescending(x => x.KnownCost).ThenByDescending(x => x.Total).ToList();
    }
    private sealed class SummaryBuilder
    {
        public long Input, Cached, Output, Total;
        public decimal Cost;
        public int Events, Unpriced, Qualified;
        public HashSet<string> Notes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, EffortBuilder> Efforts { get; } = new(StringComparer.Ordinal);
    }
    private sealed class EffortBuilder
    {
        public long Total;
        public decimal Cost;
        public int Events, Unpriced;
    }
    public static List<UsageEvent> Between(IEnumerable<UsageEvent> events, DateOnly from, DateOnly to, TimeZoneInfo zone) =>
        events.Where(e => IsBetween(e.At, from, to, zone)).ToList();
    private static bool IsBetween(DateTimeOffset at, DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        var day = Day(at, zone); return day >= from && day <= to;
    }
    public static string Compact(long n, IFormatProvider? culture = null) => n >= 1_000_000_000 ? (n / 1_000_000_000d).ToString("0.##", culture) + "B" :
        n >= 1_000_000 ? (n / 1_000_000d).ToString("0.##", culture) + "M" : n >= 1000 ? (n / 1000d).ToString("0.#", culture) + "K" : n.ToString("N0", culture);
    public static string Dollars(decimal n, IFormatProvider? culture = null) => "$" + n.ToString("N2", culture ?? CultureInfo.CurrentCulture);
}

// Stable pseudonymous identity for separating local accounts; never persist the raw account id.
public static class AccountIdentity
{
    public static string? Key(string? id) => string.IsNullOrWhiteSpace(id) ? null :
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id.Trim())));
    public static string Label(string? key) => key is { Length: >= 8 } ? F("LocalAccount", key[..8]) : T("UnknownAccount");
}

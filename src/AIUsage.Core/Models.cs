using System.Globalization;
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
public sealed record Tokens(long Input, long Cached, long Output, long Reasoning, long Total, long CacheWrite = 0)
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
            Math.Max(0, Total - previous.Total), Math.Max(0, CacheWrite - previous.CacheWrite));
    }
}

public sealed record UsageEvent(DateTimeOffset At, string Model, Tokens Tokens, string Tier = "standard");
public sealed record LimitWindow(string Name, double UsedPercent, DateTimeOffset? ResetAt, long? Seconds);
public sealed record QuotaSnapshot(DateTimeOffset At, string Source, string? Plan, List<LimitWindow> Windows,
    string? Credits = null, long? ResetCredits = null);
public sealed record ScanResult(List<UsageEvent> Events, List<QuotaSnapshot> Quotas, int Files, int Warnings, DateTimeOffset At);
public sealed record ModelSummary(string Model, long Input, long Cached, long Output, long Total, decimal KnownCost, int Unpriced, int Events);

public static class UsageSummary
{
    public static DateOnly Day(DateTimeOffset at, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);
    public static List<ModelSummary> Group(IEnumerable<UsageEvent> source, PriceCatalog prices) => source
        .GroupBy(e => e.Model, StringComparer.Ordinal)
        .Select(g => new ModelSummary(g.Key, g.Sum(e => e.Tokens.Input), g.Sum(e => e.Tokens.Cached),
            g.Sum(e => e.Tokens.Output), g.Sum(e => e.Tokens.Total), g.Sum(e => prices.Cost(e) ?? 0),
            g.Count(e => prices.Cost(e) is null), g.Count()))
        .OrderByDescending(g => g.KnownCost).ThenByDescending(g => g.Total).ToList();
    public static List<UsageEvent> Between(IEnumerable<UsageEvent> events, DateOnly from, DateOnly to, TimeZoneInfo zone) =>
        events.Where(e => Day(e.At, zone) >= from && Day(e.At, zone) <= to).ToList();
    public static string Compact(long n) => n >= 1_000_000_000 ? $"{n / 1_000_000_000d:0.##}B" :
        n >= 1_000_000 ? $"{n / 1_000_000d:0.##}M" : n >= 1000 ? $"{n / 1000d:0.#}K" : n.ToString("N0");
    public static string Dollars(decimal n) => "$" + n.ToString("N2", CultureInfo.InvariantCulture);
}

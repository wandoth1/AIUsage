using static AIUsage.Core.L10n;
// Duration-based window mapping adapted from OpenUsage v0.7.12 (MIT).
using System.Globalization;
using System.Text.Json;

namespace AIUsage.Core;

public static class QuotaParser
{
    public static QuotaSnapshot Parse(JsonElement body, DateTimeOffset at, string source, string? accountKey = null)
    {
        var windows = new List<LimitWindow>();
        AddProvider(body, windows, at, "codex");
        var additional = body.Get("additional_rate_limits");
        if (additional.ValueKind == JsonValueKind.Array)
            foreach (var item in additional.EnumerateArray().Take(100)) AddProvider(item, windows, at, "additional");
        var credits = body.Get("credits");
        var balance = credits.Get("balance");
        string? creditText = balance.ValueKind == JsonValueKind.String ? balance.GetString() : balance.Number()?.ToString(CultureInfo.InvariantCulture);
        if (credits.Get("unlimited").ValueKind == JsonValueKind.True) creditText = "Unlimited";
        var resets = body.Get("rate_limit_reset_credits").Get("available_count").Number();
        return new(at, source, body.Text("plan_type"), windows, creditText,
            resets.HasValue ? (long)Math.Clamp(resets.Value, 0, 1_000_000_000_000_000d) : null, accountKey);
    }
    private static void AddProvider(JsonElement body, List<LimitWindow> result, DateTimeOffset at, string fallback)
    {
        string? suppliedId = body.Text("limit_id") ?? body.Text("rate_limit_id");
        string? suppliedName = body.Text("limit_name") ?? body.Text("rate_limit_name");
        string id = (suppliedId ?? suppliedName ?? fallback).Trim().ToLowerInvariant();
        if (id.Length == 0) id = fallback;
        string prefix = (suppliedName ?? id).Contains("spark", StringComparison.OrdinalIgnoreCase) || id == "codex_bengalfox"
            ? "Spark" : id == "codex" ? "Codex" : suppliedName ?? id;
        var rates = body.Get("rate_limit");
        Add(rates.ValueKind == JsonValueKind.Object ? rates : body, id, prefix, result, at);
    }
    private static void Add(JsonElement limits, string id, string prefix, List<LimitWindow> result, DateTimeOffset at)
    {
        foreach (var (key, fallback) in new[] { ("primary", "Session"), ("secondary", "Weekly") })
        {
            var window = limits.Get(key + "_window");
            if (window.ValueKind != JsonValueKind.Object) window = limits.Get(key);
            var used = window.Get("used_percent").Number();
            if (!used.HasValue) continue; // Missing is unknown, never zero.
            var seconds = window.Get("limit_window_seconds").Number();
            var minutes = window.Get("window_minutes").Number();
            seconds ??= minutes.HasValue ? minutes * 60 : null;
            if (seconds is not (> 0 and <= 315360000)) seconds = null;
            string name = seconds switch { >= 518400 and <= 691200 => "Weekly", >= 14400 and <= 21600 => "Session", > 0 => (seconds.Value / 3600).ToString("0.#", CultureInfo.InvariantCulture) + " h", _ => fallback };
            var reset = window.Get("reset_at").Date() ?? window.Get("resets_at").Date();
            var after = window.Get("reset_after_seconds").Number() ?? window.Get("resets_in_seconds").Number();
            if (reset is null && after is >= 0 and < 315360000 && (DateTimeOffset.MaxValue - at).TotalSeconds >= after.Value)
                reset = at.AddSeconds(after.Value);
            // Duration, not the primary/secondary slot or translated label, identifies a window.
            string identity = id + ":" + (seconds?.ToString("R", CultureInfo.InvariantCulture) ?? key);
            result.Add(new($"{prefix} · {name}", used.Value, reset, seconds.HasValue ? (long)seconds.Value : null, identity));
        }
    }
}

public static class QuotaSelection
{
    public static List<(QuotaSnapshot Snapshot, LimitWindow Window)> Select(IEnumerable<QuotaSnapshot> local)
    {
        // These snapshots come only from the selected local rollouts. No account service exists.
        return local.SelectMany(q => q.Windows.Select(w => (Snapshot: q, Window: w)))
            .GroupBy(x => (x.Snapshot.AccountKey, Id: string.IsNullOrEmpty(x.Window.Id) ? x.Window.Name : x.Window.Id))
            .Select(g => g.OrderByDescending(x => x.Snapshot.At).First())
            .OrderBy(x => x.Snapshot.AccountKey).ThenBy(x => x.Window.Name).ToList();
    }
}

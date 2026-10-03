namespace AIUsage.Core;

// Immutable-by-convention snapshot computed off the WPF thread. Pricing is evaluated once per event.
public sealed record DashboardData(Dictionary<int, List<ModelSummary>> Periods, Dictionary<DateOnly, long> DailyTokens)
{
    public List<ModelSummary> ForPeriod(int period) => Periods.TryGetValue(period, out var rows) ? rows : [];
    public static DashboardData Create(IEnumerable<UsageEvent> events, PriceCatalog prices, TimeZoneInfo zone, DateOnly today)
    {
        var days = events.GroupBy(e => UsageSummary.Day(e.At, zone)).ToDictionary(g => g.Key, g => UsageSummary.Group(g, prices));
        var totals = days.ToDictionary(d => d.Key, d => d.Value.Sum(r => r.Total));
        var periods = new Dictionary<int, List<ModelSummary>>();
        foreach (int period in new[] { -1, 1, 7, 30 })
        {
            var end = period == -1 ? today.AddDays(-1) : today;
            var start = period <= 1 ? end : end.AddDays(1 - period);
            periods[period] = days.Where(d => d.Key >= start && d.Key <= end).SelectMany(d => d.Value)
                .GroupBy(r => r.Model, StringComparer.Ordinal).Select(g => new ModelSummary(g.Key,
                    g.Sum(r => r.Input), g.Sum(r => r.Cached), g.Sum(r => r.Output), g.Sum(r => r.Total),
                    g.Sum(r => r.KnownCost), g.Sum(r => r.Unpriced), g.Sum(r => r.Events), g.Sum(r => r.Qualified),
                    string.Join(" | ", g.Select(r => r.PricingNotes).Where(n => n.Length > 0).Distinct()),
                    EffortSummary.Merge(g.SelectMany(r => r.EffortRows))))
                .OrderByDescending(r => r.KnownCost).ThenByDescending(r => r.Total).ToList();
        }
        return new(periods, totals);
    }
}

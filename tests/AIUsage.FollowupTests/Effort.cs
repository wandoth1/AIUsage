using System.Text;
using AIUsage.Core;

// Cost by reasoning effort. Synthetic rollouts and transcripts in temporary folders only.
internal static partial class Program
{
    private static void EffortCases()
    {
        Test("Effort: Codex takes it from turn_context and thread settings, like the model", () =>
        {
            using var t = new Home();
            t.Write(Meta() + Row("turn_context", new { model = "gpt-6.1-sol", effort = "High" }) + Count(1000, 100) +
                Row("event_msg", new { type = "thread_settings_applied", thread_settings = new { model = "gpt-6.1-sol", reasoning_effort = "max" } }) + Count(3000, 300) +
                Row("turn_context", new { model = "gpt-6.1-sol", collaboration_mode = new { settings = new { reasoning_effort = "low" } } }) + Count(3500, 350));
            var events = new LogScanner(t.Cache).Scan(t.Root).Events;
            Equal(3, events.Count); Equal("high", events[0].Effort); Equal("max", events[1].Effort); Equal("low", events[2].Effort);
            Equal(2200L, events[1].Tokens.Total);
        });
        Test("Effort: identical Codex copies are still counted once", () =>
        {
            using var t = new Home();
            string text = Meta() + Row("turn_context", new { model = "gpt-6.1-sol", effort = "high" }) + Count(1000, 100);
            t.Write(text); File.WriteAllText(Path.Combine(t.Root, "sessions", "copy.jsonl"), text, new UTF8Encoding(false));
            Equal(1100L, new LogScanner(t.Cache).Scan(t.Root).Events.Sum(e => e.Tokens.Total));
        });
        Test("Effort: Claude records keep their effort; invalid or missing values are 'not recorded'", () =>
        {
            using var t = new ClaudeHome();
            t.Write(@"projects\p\e.jsonl", Claude("msg_e1", "req_e1", "claude-opus-5-5", 100, 10, effort: "xhigh") +
                Claude("msg_e2", "req_e2", "claude-opus-5-5", 100, 10, effort: "<b>") + Claude("msg_e3", "req_e3", "claude-opus-5-5", 100, 10));
            var events = t.Scan().Events;
            Equal("xhigh", events[0].Effort); Equal(null, events[1].Effort); Equal(null, events[2].Effort);
        });
        Test("Effort: a cache with a malformed effort is rebuilt", () =>
        {
            using var t = new ClaudeHome();
            t.Write(@"projects\p\c.jsonl", Claude("msg_c", "req_c", "claude-opus-5", 100, 10, effort: "high"));
            new ClaudeLogScanner(t.Cache).Scan(t.Root);
            string cacheFile = Directory.GetFiles(t.Cache, "*.json").Single();
            File.WriteAllText(cacheFile, File.ReadAllText(cacheFile).Replace("\"Effort\":\"high\"", "\"Effort\":\"HIGH!\""));
            var fresh = new ClaudeLogScanner(t.Cache); var scan = fresh.Scan(t.Root);
            Equal(0, fresh.PersistentCacheHits); Equal("high", scan.Events[0].Effort);
        });
        Test("Effort: per-effort rows add up to the model row across a period", () =>
        {
            var zone = TimeZoneInfo.Utc; var today = new DateOnly(2026, 10, 3);
            DateTimeOffset Day(int back) => new(today.AddDays(-back).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
            var events = new List<UsageEvent>
            {
                new(Day(0), "claude-opus-5", new Tokens(1_000_000, 0, 0, 0, 1_000_000), Effort: "max"),
                new(Day(1), "claude-opus-5", new Tokens(1_000_000, 0, 0, 0, 1_000_000), Effort: "max"),
                new(Day(1), "claude-opus-5", new Tokens(0, 0, 1_000_000, 0, 1_000_000), Effort: "high"),
                new(Day(2), "claude-opus-5", new Tokens(2_000_000, 0, 0, 0, 2_000_000)),
            };
            var row = DashboardData.Create(events, ClaudePrices, zone, today).ForPeriod(7).Single();
            var efforts = row.EffortRows;
            Equal(3, efforts.Count);
            Equal("max", efforts[0].Effort); Equal(10m, efforts[0].KnownCost); Equal(2, efforts[0].Events);   // 2M input × $5
            Equal("high", efforts[1].Effort); Equal(25m, efforts[1].KnownCost);                              // 1M output × $25
            Equal(null, efforts[2].Effort); Equal(10m, efforts[2].KnownCost);
            Equal(row.KnownCost, efforts.Sum(e => e.KnownCost)); Equal(row.Total, efforts.Sum(e => e.Total)); Equal(row.Events, efforts.Sum(e => e.Events));
            Equal(1, DashboardData.Create(events, ClaudePrices, zone, today).ForPeriod(1).Single().EffortRows.Count);
        });
        Test("Effort: normalisation and ordering", () =>
        {
            Equal("xhigh", EffortSummary.Normalize(" XHigh ")); Equal(null, EffortSummary.Normalize("high!")); Equal(null, EffortSummary.Normalize(new string('a', 17)));
            Require(EffortSummary.Rank("max") < EffortSummary.Rank("high") && EffortSummary.Rank("low") < EffortSummary.Rank("custom") && EffortSummary.Rank("custom") < EffortSummary.Rank(null), "Order");
        });
    }
}

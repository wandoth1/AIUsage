using System.Globalization;
using System.Text;
using System.Text.Json;
using AIUsage.Core;

internal static partial class Program
{
    private static Task Extended()
    {
        Check("H01 provider ids, not matching display names, identify windows", () =>
        {
            var one = Q("{\"limit_id\":\"a\",\"limit_name\":\"Same\",\"primary\":{\"used_percent\":5,\"window_minutes\":300}}");
            var two = Q("{\"limit_id\":\"b\",\"limit_name\":\"Same\",\"primary\":{\"used_percent\":80,\"window_minutes\":300}}");
            Equal(2, QuotaSelection.Select([one, two]).Count);
        });
        Check("H01 same weekly window moved between slots has one identity", () =>
        {
            var one = Q("{\"primary\":{\"used_percent\":5,\"window_minutes\":10080}}");
            var two = Q("{\"secondary\":{\"used_percent\":10,\"window_minutes\":10080}}") with { At = At.AddSeconds(1) };
            Equal(1, QuotaSelection.Select([one, two]).Count);
            Equal(10d, QuotaSelection.Select([one, two])[0].Window.UsedPercent);
        });
        Check("H16 distinct local account identities remain separate", () =>
        {
            var q = Q("{\"primary\":{\"used_percent\":5}}");
            Equal(2, QuotaSelection.Select([q with { AccountKey = "a" }, q with { AccountKey = "b" }]).Count);
        });
        Check("H02 null tier resets, partial context omission does not", () =>
        {
            var p = new CodexParser(); Parse(p, Row("turn_context", new { model = "gpt-6.1-sol", service_tier = "priority" }));
            Parse(p, Context()); Equal("priority", Parse(p, Count(1000, 100))!.Tier);
            Parse(p, Row("turn_context", new { model = "gpt-6.1-sol", service_tier = (string?)null }));
            Equal("standard", Parse(p, Count(2000, 200))!.Tier);
        });
        Check("H02 copied parent settings cannot overwrite the child owner", () =>
        {
            var p = new CodexParser(); Parse(p, Row("session_meta", new { id = "child" })); Parse(p, Context());
            Parse(p, Row("event_msg", new { type = "thread_settings_applied", thread_id = "parent", thread_settings = new { model = "gpt-6-astra", service_tier = "priority" } }));
            var e = Parse(p, Count(1000, 100))!; Equal("gpt-6.1-sol", e.Model); Equal("standard", e.Tier);
        });
        Check("H06 missing started_at warns once without charging replay", () =>
        {
            var p = new CodexParser(); Parse(p, Row("session_meta", new { id = "child", forked_from_id = "parent" }));
            for (int i = 0; i < 2; i++) Parse(p, Row("event_msg", new { type = "task_started" }));
            Equal<UsageEvent?>(null, Parse(p, Count(9000, 100))); Equal(1, p.Warnings);
            Parse(p, Row("event_msg", new { type = "task_started", started_at = At.ToUnixTimeSeconds() }));
            Equal(1000L, Parse(p, Count(10000, 200))!.Tokens.Input);
        });
        Check("H05 quarantine persists across launches, not just first read", () =>
        {
            using var t = new Home(); t.Write(Row("session_meta", new { id = "child", text = new string('x', LogScanner.MaxRecordBytes), forked_from_id = "parent" }) + Context() + Count(1000, 100));
            var a = new LogScanner(t.Cache).Scan(t.Root); var b = new LogScanner(t.Cache).Scan(t.Root);
            Equal(0, a.Events.Count); Equal(0, b.Events.Count); Assert(b.Warnings > 0, "Missing persisted warning");
        });
        Check("H07 real repeated equal-sized turns in same second survive", () =>
        {
            using var t = new Home(); t.Write(Row("session_meta", new { id = "a" }) + Context() + Count(1000, 100) + Count(2000, 200));
            var r = new LogScanner(t.Cache).Scan(t.Root); Equal(2, r.Events.Count); Equal(2200L, r.Events.Sum(e => e.Tokens.Total));
        });
        Check("H07 metadata-less independent files are not assumed copies", () =>
        {
            using var t = new Home(); t.Write(Context() + Count(1000, 100)); File.Copy(t.Rollout, Path.Combine(t.Sessions, "unknown.jsonl"));
            Equal(2, new LogScanner(t.Cache).Scan(t.Root).Events.Count);
        });
        Check("H08 duplicates and misspelled custom tariff fields are rejected", () =>
        {
            using var t = new Home(); string path = Path.Combine(t.Root, "p.json");
            foreach (var body in new[] { "{\"a\":{\"Input\":2,\"Cached\":1,\"Output\":3,\"input\":4}}", "{\"a\":{\"Input\":2,\"Cached\":1,\"Output\":3},\"A\":{\"Input\":2,\"Cached\":1,\"Output\":3}}", "{\"a\":{\"Input\":2,\"Cached\":1,\"Output\":3,\"FastMultiplir\":2}}" })
            { File.WriteAllText(path, body); bool rejected = false; try { PriceCatalog.Load(path); } catch (JsonException) { rejected = true; } Assert(rejected, "Invalid override accepted"); }
        });
        Check("H09 alias equivalence and custom rates are explicitly qualified", () =>
        {
            var e = new UsageEvent(At, "gpt-reserve", new(1000, 0, 100, 0, 1100));
            var q = PriceCatalog.Load().Quote(e); Equal("gpt-5.6-luna", q.PricingModel); Assert(q.Note.Contains("Inherited OpenUsage alias"), "Missing alias note");
            using var t = new Home(); string path = Path.Combine(t.Root, "p.json");
            File.WriteAllText(path, "{\"gpt-reserve\":{\"Input\":2,\"Cached\":1,\"Output\":10}}");
            q = PriceCatalog.Load(path).Quote(e); Equal("gpt-reserve", q.PricingModel); Equal(0.003m, q.Cost); Assert(q.Note.Contains("User-supplied custom price"), "Missing override note");
        });
        Check("H03 current Sol promo and historical repricing are labelled", () =>
        {
            var q = PriceCatalog.Load().Quote(new(At, "gpt-5.6-sol", new(200000, 0, 100000, 0, 300000)));
            Equal(2.8m, q.Cost); Assert(q.Note.Contains("2026-11-21"), "Missing dated promotion warning");
        });
        Check("H09 qualified estimates are carried into CSV, not only UI", () =>
        {
            var csv = CsvExport.Build([new(At, "gpt-reserve", new(1000, 0, 100, 0, 1100))], PriceCatalog.Load(), TimeZoneInfo.Utc);
            Assert(csv.Contains("qualified_events,pricing_notes") && csv.Contains("Inherited OpenUsage alias"), "Missing provenance in CSV");
        });
        Check("H11 missing schema field is not trusted as current", () =>
        {
            using var t = new Home(); t.Write(Context() + Count(1000, 100)); new LogScanner(t.Cache).Scan(t.Root);
            var path = Directory.GetFiles(t.Cache, "*.json").Single(); var c = JsonSerializer.Deserialize<FileCache>(File.ReadAllText(path))!;
            c.Events.Clear(); c.Schema = 0; AtomicJson.Write(path, c); Equal(1, new LogScanner(t.Cache).Scan(t.Root).Events.Count);
        });
        Check("H13 EOF preview is retracted when incomplete bytes are appended", () =>
        {
            using var t = new Home(); t.Write(Context() + Count(1000, 100).TrimEnd('\n')); var s = new LogScanner(t.Cache);
            s.Scan(t.Root); Equal(1, s.Scan(t.Root).Events.Count);
            File.AppendAllText(t.Rollout, "broken"); Equal(0, s.Scan(t.Root).Events.Count);
            Equal(0, new LogScanner(t.Cache).Scan(t.Root).Events.Count);
        });
        Check("H13 valid EOF preview survives launch and later append exactly once", () =>
        {
            using var t = new Home(); t.Write(Context() + Count(1000, 100).TrimEnd('\n')); new LogScanner(t.Cache).Scan(t.Root);
            Equal(1, new LogScanner(t.Cache).Scan(t.Root).Events.Count);
            File.AppendAllText(t.Rollout, "\n" + Count(2000, 200));
            var r = new LogScanner(t.Cache).Scan(t.Root); Equal(2, r.Events.Count); Equal(2200L, r.Events.Sum(e => e.Tokens.Total));
        });
        Check("H20 UI currency respects Spanish decimal conventions", () =>
        {
            var old = CultureInfo.CurrentCulture; try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES"); Equal("$1.234,50", UsageSummary.Dollars(1234.5m)); } finally { CultureInfo.CurrentCulture = old; }
        });
        Check("Account identifiers are not cached in plaintext", () =>
        {
            using var t = new Home(); t.Write(Row("session_meta", new { id = "s", creator_account_id = "SYNTHETIC-ACCOUNT-PRIVATE" }) + Context() + Count(1000, 100));
            new LogScanner(t.Cache).Scan(t.Root);
            Assert(!string.Join("", Directory.GetFiles(t.Cache).Select(File.ReadAllText)).Contains("SYNTHETIC-ACCOUNT-PRIVATE"), "Raw account id leaked");
        });
        Check("Concurrent atomic writes leave valid JSON and no temp files", () =>
        {
            using var t = new Home(); string path = Path.Combine(t.Root, "shared.json");
            Parallel.For(0, 12, i => AtomicJson.Write(path, new { Value = i }));
            using var doc = JsonDocument.Parse(File.ReadAllText(path)); Assert(doc.RootElement.GetProperty("Value").GetInt32() >= 0, "Invalid result"); Equal(0, Directory.GetFiles(t.Root, "*.tmp").Length);
        });
        Check("Incremental random chunks reconcile with a complete scan", () =>
        {
            var random = new Random(12345);
            for (int trial = 0; trial < 12; trial++)
            {
                using var a = new Home(); using var b = new Home();
                string text = Row("session_meta", new { id = "same" }) + Context() + string.Concat(Enumerable.Range(1, 20).Select(i => Count(i * 1000, i * 100, At.AddSeconds(i))));
                a.Write(text); b.Write(""); var inc = new LogScanner(b.Cache);
                for (int offset = 0; offset < text.Length;) { int n = Math.Min(random.Next(1, 900), text.Length - offset); File.AppendAllText(b.Rollout, text.Substring(offset, n)); offset += n; inc.Scan(b.Root); }
                var full = new LogScanner(a.Cache).Scan(a.Root); var result = inc.Scan(b.Root);
                Equal(20, result.Events.Count); Assert(full.Events.SequenceEqual(result.Events), "Incremental mismatch");
            }
        });
        Check("H18 dashboard summaries reconcile with raw grouping in every period", () =>
        {
            var events = Enumerable.Range(0, 35).Select(i => new UsageEvent(At.AddDays(-i), i % 2 == 0 ? "gpt-6.1-sol" : "unknown", new(1000, 200, 100, 0, 1100))).ToList();
            var today = UsageSummary.Day(At, TimeZoneInfo.Utc); var prices = PriceCatalog.Load();
            var data = DashboardData.Create(events, prices, TimeZoneInfo.Utc, today);
            foreach (int period in new[] { -1, 1, 7, 30 })
            {
                var end = period == -1 ? today.AddDays(-1) : today;
                var start = period <= 1 ? end : end.AddDays(1 - period);
                var expected = UsageSummary.Group(UsageSummary.Between(events, start, end, TimeZoneInfo.Utc), prices);
                Assert(expected.SequenceEqual(data.ForPeriod(period)), "Summary mismatch");
            }
        });
        Check("H14 explicit cache rebuild ignores all previous metadata", () =>
        {
            using var t = new Home(); t.Write(Context() + Count(1000, 100)); var s = new LogScanner(t.Cache);
            s.Scan(t.Root); s.ClearCache(); Equal(0, Directory.GetFiles(t.Cache, "*.json").Length);
            Equal(1, s.Scan(t.Root).Events.Count); Assert(File.Exists(t.Rollout), "Source rollout was removed");
        });
        return Task.CompletedTask;
    }
    private static QuotaSnapshot Q(string json) { using var d = JsonDocument.Parse(json); return QuotaParser.Parse(d.RootElement, At, "test"); }
}

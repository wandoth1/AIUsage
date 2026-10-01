using System.Globalization;
using System.Text;
using System.Text.Json;
using AIUsage.Core;

// Dependency-free regression runner. All fixtures are synthetic; never reads the user's Codex home.
internal static class Program
{
    private static readonly DateTimeOffset Stamp = DateTimeOffset.UtcNow.AddHours(-1);
    private static int passed, failed;
    public static int Main()
    {
        L10n.SetLanguage("en");
        Test("Reasoning and cached input are subsets, not extra tokens", () =>
        {
            var t = ReadTokens("{\"input_tokens\":100,\"cached_input_tokens\":80,\"output_tokens\":20,\"reasoning_output_tokens\":10}");
            Eq(120L, t.Total); Eq(80L, t.Cached); Eq(10L, t.Reasoning);
        });
        Test("Explicit total is preserved", () => Eq(777L, ReadTokens("{\"input_tokens\":100,\"output_tokens\":20,\"total_tokens\":777}").Total));
        Test("Legacy token fields are accepted", () =>
        {
            var t = ReadTokens("{\"prompt_tokens\":100,\"cache_read_input_tokens\":40,\"completion_tokens\":20}");
            Eq(100L, t.Input); Eq(40L, t.Cached); Eq(20L, t.Output);
        });
        Test("Negative and excessive cached counts are clamped", () =>
        {
            var t = ReadTokens("{\"input_tokens\":20,\"cached_input_tokens\":90,\"output_tokens\":-5}");
            Eq(20L, t.Cached); Eq(0L, t.Output);
        });
        Test("Token deltas are non-negative", () => Eq(0L, new Tokens(5, 0, 2, 0, 7).Delta(new(20, 0, 10, 0, 30)).Input));
        Test("Turn context supplies model and last usage wins", () =>
        {
            var p = new CodexParser(); Parse(p, Context("gpt-6-astra"));
            var e = Parse(p, Count(Raw(1000, 800, 100), Raw(100, 80, 10)))!;
            Eq("gpt-6-astra", e.Model); Eq(100L, e.Tokens.Input);
        });
        Test("Repeated cumulative snapshot does not charge last usage twice", () =>
        {
            var p = new CodexParser(); var row = Count(Raw(100, 80, 10), Raw(100, 80, 10));
            NotNull(Parse(p, row)); IsNull(Parse(p, row));
        });
        Test("Missing last usage derives cumulative delta", () =>
        {
            var p = new CodexParser(); Parse(p, Count(Raw(100, 80, 10)));
            var e = Parse(p, Count(Raw(160, 120, 25)))!;
            Eq(60L, e.Tokens.Input); Eq(40L, e.Tokens.Cached); Eq(15L, e.Tokens.Output); Eq(75L, e.Tokens.Total);
        });
        Test("Model can change within one rollout", () =>
        {
            var p = new CodexParser(); Parse(p, Context("gpt-6-astra")); Parse(p, Count(Raw(100, 80, 10)));
            Parse(p, Context("gpt-6.1-sol")); Eq("gpt-6.1-sol", Parse(p, Count(Raw(200, 160, 20)))!.Model);
        });
        Test("No model metadata stays unknown, not a guessed model", () => Eq("unknown", Parse(new(), Count(Raw(100, 0, 10)))!.Model));
        Test("Fast tier is read from each log, never current config", () =>
        {
            var p = new CodexParser(); Parse(p, Row("event_msg", new { type = "thread_settings_applied", thread_settings = new { service_tier = "priority" } }));
            Eq("priority", Parse(p, Count(Raw(100, 0, 10)))!.Tier);
            Parse(p, Row("event_msg", new { type = "thread_settings_applied", service_tier = "default" }));
            Eq("default", Parse(p, Count(Raw(200, 0, 20)))!.Tier);
        });
        Test("Fork skips replayed history and seeds delta baseline", () =>
        {
            var p = new CodexParser(); Parse(p, Row("session_meta", new { forked_from_id = "synthetic-parent" }));
            Parse(p, Context("gpt-6.1-sol")); IsNull(Parse(p, Count(Raw(100, 80, 10))));
            Parse(p, Row("event_msg", new { type = "task_started", started_at = Stamp.AddMinutes(-10).ToUnixTimeSeconds() }));
            IsNull(Parse(p, Count(Raw(150, 120, 15), null, Stamp.AddSeconds(5))));
            Parse(p, Row("event_msg", new { type = "task_started", started_at = Stamp.ToUnixTimeSeconds() }));
            Eq(50L, Parse(p, Count(Raw(200, 160, 20)))!.Tokens.Input);
        });
        Test("Null fork metadata is not a child session", () =>
        {
            var p = new CodexParser(); Parse(p, Row("session_meta", new { forked_from_id = (string?)null, parent_thread_id = " " }));
            NotNull(Parse(p, Count(Raw(100, 0, 10))));
        });
        Test("Missing child creation time waits for self-timed live task", () =>
        {
            var p = new CodexParser(); Parse(p, "{\"type\":\"session_meta\",\"payload\":{\"parent_thread_id\":\"synthetic-parent\"}}");
            IsNull(Parse(p, Count(Raw(100, 80, 10))));
            Parse(p, Row("event_msg", new { type = "task_started", started_at = Stamp.ToUnixTimeSeconds() }));
            Eq(50L, Parse(p, Count(Raw(150, 100, 20)))!.Tokens.Input);
        });
        Test("Nested subagent marker arms replay gate", () =>
        {
            var p = new CodexParser(); Parse(p, Row("session_meta", new { source = new { subagent = new { thread_spawn = "synthetic-parent" } } }));
            IsNull(Parse(p, Count(Raw(100, 0, 10))));
        });
        Test("Repeated parent metadata cannot replace own child identity", () =>
        {
            var p = new CodexParser(); Parse(p, Row("session_meta", new { forked_from_id = "synthetic-parent" }));
            Parse(p, Row("session_meta", new { forked_from_id = (string?)null }));
            IsNull(Parse(p, Count(Raw(100, 0, 10))));
        });
        Test("Malformed accounting records warn, not crash", () =>
        {
            var p = new CodexParser(); IsNull(Parse(p, "{\"type\":\"token_count\"")); Eq(1, p.Warnings);
        });
        Test("Parser states never leak across files", () =>
        {
            var p = new CodexParser(); Parse(p, Context("gpt-6-astra"));
            Eq("unknown", Parse(new(), Count(Raw(100, 0, 10)))!.Model);
        });
        Test("Decreasing totals without last usage produce warning", () =>
        {
            var p = new CodexParser(); Parse(p, Count(Raw(100, 0, 10))); Parse(p, Count(Raw(50, 0, 8))); Eq(1, p.Warnings);
        });
        Test("Sol cost prices cached input separately", () =>
        {
            var e = new UsageEvent(Stamp, "gpt-6.1-sol", new(100000, 80000, 1000, 800, 101000));
            Eq(0.058m, PriceCatalog.Load().Cost(e)!.Value);
        });
        Test("Long-context boundary is strictly greater than 272k", () =>
        {
            var c = PriceCatalog.Load();
            Eq(0.544m, c.Cost(new(Stamp, "gpt-6.1-sol", new(272000, 0, 0, 0, 272000)))!.Value);
            Eq(1.088004m, c.Cost(new(Stamp, "gpt-6.1-sol", new(272001, 0, 0, 0, 272001)))!.Value);
        });
        Test("Fast alias and priority tier multiply once", () =>
        {
            var e = new UsageEvent(Stamp, "gpt-6.1-sol-fast", new(1000, 0, 100, 0, 1100), "priority");
            Eq(0.006m, PriceCatalog.Load().Cost(e)!.Value);
        });
        Test("Dated model slug resolves without rewriting displayed name", () =>
        {
            var e = new UsageEvent(Stamp, "openai/gpt-6.1-sol-2026-09-24", new(1000, 0, 100, 0, 1100));
            Eq(0.003m, PriceCatalog.Load().Cost(e)!.Value); Eq("openai/gpt-6.1-sol-2026-09-24", e.Model);
        });
        Test("Unknown model and unknown service tier remain unpriced", () =>
        {
            var c = PriceCatalog.Load(); IsNull(c.Cost(new(Stamp, "unpublished-model", new(1, 0, 1, 0, 2))));
            IsNull(c.Cost(new(Stamp, "gpt-6.1-sol", new(1, 0, 1, 0, 2), "future-tier")));
        });
        Test("Reasoning is never charged in addition to output", () =>
        {
            var c = PriceCatalog.Load();
            Eq(c.Cost(new(Stamp, "gpt-6.1-sol", new(0, 0, 100, 0, 100))), c.Cost(new(Stamp, "gpt-6.1-sol", new(0, 0, 100, 99, 100))));
        });
        Test("Cache writes are a disjoint input bucket", () =>
            Eq(0.0025m, PriceCatalog.Load().Cost(new(Stamp, "gpt-6.1-sol", new(1000, 0, 0, 0, 1000, 1000)))!.Value));
        Test("Partial pricing still counts all tokens", () =>
        {
            var rows = UsageSummary.Group([new(Stamp, "unpublished-model", new(10, 0, 1, 0, 11))], PriceCatalog.Load());
            Eq(11L, rows[0].Total); Eq(1, rows[0].Unpriced);
        });
        Test("Weekly primary window never invents a session window", () =>
        {
            var q = Quota("{\"rate_limit\":{\"primary_window\":{\"used_percent\":24,\"limit_window_seconds\":604800}}}");
            Eq(1, q.Windows.Count); Eq("Codex · Weekly", q.Windows[0].Name);
        });
        Test("Missing used_percent is unknown, not zero", () => Eq(0, Quota("{\"rate_limit\":{\"primary_window\":{\"limit_window_seconds\":18000}}}").Windows.Count));
        Test("Raw over-limit percentage is preserved", () => Eq(102d, Quota("{\"primary\":{\"used_percent\":102,\"window_minutes\":300}}").Windows[0].UsedPercent));
        Test("Additional Spark weekly limit is discovered", () =>
        {
            var q = Quota("{\"additional_rate_limits\":[{\"limit_name\":\"GPT-5.3-Codex-Spark\",\"rate_limit\":{\"primary_window\":{\"used_percent\":15,\"limit_window_seconds\":604800}}}]}");
            Eq("Spark · Weekly", q.Windows[0].Name);
        });
        Test("Relative reset duration uses observation timestamp", () =>
        {
            var q = Quota("{\"primary\":{\"used_percent\":10,\"reset_after_seconds\":120}}"); Eq(Stamp.AddSeconds(120), q.Windows[0].ResetAt!.Value);
        });
        Test("Quota-only token record updates limits without fabricated usage", () =>
        {
            var p = new CodexParser();
            var row = Row("event_msg", new { type = "token_count", info = (object?)null, rate_limits = new { primary = new { used_percent = 10, window_minutes = 300 } } });
            IsNull(p.Parse(Encoding.UTF8.GetBytes(row), out var q)); NotNull(q); Eq(1, q!.Windows.Count);
        });
        Test("Days use configured local timezone, not UTC date", () =>
        {
            var zone = TimeZoneInfo.CreateCustomTimeZone("TestUTC+2", TimeSpan.FromHours(2), "Test", "Test");
            Eq(new DateOnly(2026, 10, 2), UsageSummary.Day(new(2026, 10, 1, 23, 30, 0, TimeSpan.Zero), zone));
        });
        Test("CSV formula injection is escaped", () =>
        {
            Eq("\"'=1+1\"", CsvExport.Escape("=1+1")); Eq("\"'  @SUM(1)\"", CsvExport.Escape("  @SUM(1)"));
            Eq("\"safe\"\"name\"", CsvExport.Escape("safe\"name"));
        });
        Test("CSV dollar values are invariant under Spanish locale", () =>
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
                string csv = CsvExport.Build([new(Stamp, "gpt-6.1-sol", new(1000, 0, 100, 0, 1100))], PriceCatalog.Load(), TimeZoneInfo.Utc);
                True(csv.Contains("0.003000,0", StringComparison.Ordinal));
            }
            finally { CultureInfo.CurrentCulture = original; }
        });
        Test("Scanner reads active rollouts with BOM and whitespace", () =>
        {
            using var t = new TemporaryHome();
            File.WriteAllText(t.Rollout, "\uFEFF" + Context("gpt-6.1-sol") + "\n" + Count(Raw(100, 80, 10)) + "\n");
            var result = new LogScanner(t.Cache).Scan(t.Home); Eq(1, result.Events.Count); Eq(0, result.Warnings);
        });
        Test("Trailing partial JSON is retried after append", () =>
        {
            using var t = new TemporaryHome(); var scanner = new LogScanner(t.Cache);
            string count = Count(Raw(100, 80, 10)); int middle = count.Length / 2;
            File.WriteAllText(t.Rollout, Context("gpt-6.1-sol") + "\n" + count[..middle]);
            Eq(0, scanner.Scan(t.Home).Events.Count);
            File.AppendAllText(t.Rollout, count[middle..] + "\n"); Eq(1, scanner.Scan(t.Home).Events.Count);
            Eq(1, scanner.Scan(t.Home).Events.Count);
        });
        Test("Persistent cache retains parser model and delta baseline", () =>
        {
            using var t = new TemporaryHome();
            File.WriteAllText(t.Rollout, Context("gpt-6.1-sol") + "\n" + Count(Raw(100, 80, 10)) + "\n");
            Eq(1, new LogScanner(t.Cache).Scan(t.Home).Events.Count);
            File.AppendAllText(t.Rollout, Count(Raw(150, 100, 20), null, Stamp.AddSeconds(1)) + "\n");
            var result = new LogScanner(t.Cache).Scan(t.Home);
            Eq(2, result.Events.Count); Eq(50L, result.Events[1].Tokens.Input); Eq("gpt-6.1-sol", result.Events[1].Model);
        });
        Test("Truncation discards obsolete cached events", () =>
        {
            using var t = new TemporaryHome(); var scanner = new LogScanner(t.Cache);
            File.WriteAllText(t.Rollout, Context("gpt-6.1-sol") + "\n" + Count(Raw(100, 80, 10)) + "\n");
            Eq(1, scanner.Scan(t.Home).Events.Count); File.WriteAllText(t.Rollout, ""); Eq(0, scanner.Scan(t.Home).Events.Count);
        });
        Test("Active copy wins over same relative archived path", () =>
        {
            using var t = new TemporaryHome(); string archive = Path.Combine(t.Home, "archived_sessions"); Directory.CreateDirectory(archive);
            File.WriteAllText(t.Rollout, Context("gpt-6.1-sol") + "\n" + Count(Raw(100, 80, 10)) + "\n");
            File.WriteAllText(Path.Combine(archive, "rollout.jsonl"), Context("gpt-6-astra") + "\n" + Count(Raw(900, 0, 50)) + "\n");
            var r = new LogScanner(t.Cache).Scan(t.Home); Eq(1, r.Events.Count); Eq(100L, r.Events[0].Tokens.Input);
        });
        Test("Identical events copied into separate files count once", () =>
        {
            using var t = new TemporaryHome();
            File.WriteAllText(t.Rollout, Row("session_meta", new { id = "same-logical-session" }) + "\n" + Context("gpt-6.1-sol") + "\n" + Count(Raw(100, 80, 10)) + "\n");
            File.Copy(t.Rollout, Path.Combine(Path.GetDirectoryName(t.Rollout)!, "copy.jsonl"));
            Eq(1, new LogScanner(t.Cache).Scan(t.Home).Events.Count);
        });
        Test("Corrupt cache is rebuilt from original rollout", () =>
        {
            using var t = new TemporaryHome();
            File.WriteAllText(t.Rollout, Context("gpt-6.1-sol") + "\n" + Count(Raw(100, 80, 10)) + "\n");
            new LogScanner(t.Cache).Scan(t.Home);
            foreach (var f in Directory.GetFiles(t.Cache)) File.WriteAllText(f, "not json");
            Eq(1, new LogScanner(t.Cache).Scan(t.Home).Events.Count);
        });
        Test("Oversized unknown accounting record is bounded and quarantined", () =>
        {
            using var t = new TemporaryHome();
            File.WriteAllText(t.Rollout, new string('x', LogScanner.MaxRecordBytes + 8) + "\n" + Context("gpt-6.1-sol") + "\n" + Count(Raw(100, 80, 10)) + "\n");
            var r = new LogScanner(t.Cache).Scan(t.Home); Eq(0, r.Events.Count); True(r.Warnings >= 1);
        });
        Test("Conversation text is not persisted in metadata cache", () =>
        {
            using var t = new TemporaryHome(); const string sentinel = "SYNTHETIC_PRIVATE_CONVERSATION_MUST_NOT_BE_CACHED";
            File.WriteAllText(t.Rollout, Row("response_item", new { text = sentinel }) + "\n" + Context("gpt-6.1-sol") + "\n" + Count(Raw(100, 80, 10)) + "\n");
            new LogScanner(t.Cache).Scan(t.Home);
            foreach (var f in Directory.GetFiles(t.Cache)) True(!File.ReadAllText(f).Contains(sentinel, StringComparison.Ordinal));
        });
        Test("No rollouts means no fabricated data", () =>
        {
            using var t = new TemporaryHome(); var r = new LogScanner(t.Cache).Scan(t.Home); Eq(0, r.Files); Eq(0, r.Events.Count);
        });
        Test("Custom prices load, validate, and mark estimates as customized", () =>
        {
            using var t = new TemporaryHome(); string path = Path.Combine(t.Root, "prices.json");
            File.WriteAllText(path, "{\"custom\":{\"Input\":1,\"Cached\":0.1,\"Output\":2}}");
            var c = PriceCatalog.Load(path); True(c.HasOverrides); Eq(0.001m, c.Cost(new(Stamp, "custom", new(1000, 0, 0, 0, 1000)))!.Value);
            File.WriteAllText(path, "{\"custom\":{\"Input\":-1,\"Cached\":0,\"Output\":2}}");
            Throws<InvalidOperationException>(() => PriceCatalog.Load(path));
        });
        Test("Atomic JSON creates complete readable settings", () =>
        {
            using var t = new TemporaryHome(); string path = Path.Combine(t.Root, "settings.json");
            AtomicJson.Write(path, new AppSettings { RefreshSeconds = 90 }); Eq(90, AppSettings.Load(path).RefreshSeconds); True(!File.Exists(path + ".tmp"));
        });
        Console.WriteLine($"\nRESULT: {passed} passed; {failed} failed.");
        return failed == 0 ? 0 : 1;
    }
    private static void Test(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + e); }
    }
    private static void Eq<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}"); }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static void IsNull(object? value) { if (value is not null) throw new Exception("Expected null"); }
    private static void NotNull(object? value) { if (value is null) throw new Exception("Expected value"); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name);
    }
    private static Tokens ReadTokens(string json) { using var doc = JsonDocument.Parse(json); return Tokens.Read(doc.RootElement); }
    private static QuotaSnapshot Quota(string json) { using var doc = JsonDocument.Parse(json); return QuotaParser.Parse(doc.RootElement, Stamp, "test"); }
    private static UsageEvent? Parse(CodexParser parser, string row) => parser.Parse(Encoding.UTF8.GetBytes(row), out _);
    private static string Row(string type, object payload, DateTimeOffset? at = null) => JsonSerializer.Serialize(new { timestamp = (at ?? Stamp).ToString("O"), type, payload });
    private static string Context(string model) => Row("turn_context", new { model });
    private static object Raw(long input, long cached, long output) => new { input_tokens = input, cached_input_tokens = cached, output_tokens = output, reasoning_output_tokens = 0, total_tokens = input + output };
    private static string Count(object? total, object? last = null, DateTimeOffset? at = null) => Row("event_msg", new { type = "token_count", info = new { total_token_usage = total, last_token_usage = last } }, at);
    private sealed class TemporaryHome : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "AIUsage-tests-" + Guid.NewGuid().ToString("N"));
        public string Home => Path.Combine(Root, "home");
        public string Cache => Path.Combine(Root, "cache");
        public string Rollout => Path.Combine(Home, "sessions", "rollout.jsonl");
        public TemporaryHome() => Directory.CreateDirectory(Path.GetDirectoryName(Rollout)!);
        public void Dispose() { try { Directory.Delete(Root, true); } catch (IOException) { } }
    }
}

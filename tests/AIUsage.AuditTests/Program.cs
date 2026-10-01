using System.Net;
using System.Text;
using System.Text.Json;
using AIUsage.Core;

// Independent audit regressions. No user files, credentials, external requests or paid services.
internal static partial class Program
{
    private static readonly DateTimeOffset At = DateTimeOffset.UtcNow.AddHours(-1);
    private static int passed, failed;
    public static async Task<int> Main()
    {
        Check("H01 local Spark quota must not replace Codex", () =>
        {
            using var t = new Home();
            t.Write(Row("session_meta", new { id = "session-a" }) + Context() +
                Row("event_msg", new { type = "token_count", rate_limits = new { limit_id = "codex", primary = new { used_percent = 20, window_minutes = 300 } } }) +
                Row("event_msg", new { type = "token_count", rate_limits = new { limit_id = "codex_bengalfox", limit_name = "GPT-5.3-Codex-Spark", primary = new { used_percent = 90, window_minutes = 300 } } }));
            var windows = new LogScanner(t.Cache).Scan(t.Root).Quotas.SelectMany(q => q.Windows).ToArray();
            Equal(20d, windows.Single(w => w.Name == "Codex · Sesión").UsedPercent);
            Equal(90d, windows.Single(w => w.Name == "Spark · Sesión").UsedPercent);
        });
        Check("H02 a complete settings snapshot without tier resets priority", () =>
        {
            var p = new CodexParser(); Parse(p, Context());
            Parse(p, Row("event_msg", new { type = "thread_settings_applied", thread_settings = new { model = "gpt-6.1-sol", service_tier = "priority" } }));
            Parse(p, Count(1000, 100));
            Parse(p, Row("event_msg", new { type = "thread_settings_applied", thread_settings = new { model = "gpt-6.1-sol" } }));
            var e = Parse(p, Count(2000, 200))!;
            Equal("standard", e.Tier); Equal(0.003m, PriceCatalog.Load().Cost(e));
        });
        Check("H03 Sol 5.6 official 2026-10-01 standard price", () =>
        {
            // https://developers.openai.com/api/docs/models/gpt-5.6-sol : 4/0.4/20 USD per million.
            Equal(2.8m, PriceCatalog.Load().Cost(new(At, "gpt-5.6-sol", new(200000, 0, 100000, 0, 300000))));
        });
        Check("H04 context-window-fill counter is not billable usage", () =>
        {
            var p = new CodexParser(); Parse(p, Context()); NotNull(Parse(p, Count(1000, 100)));
            var filler = Row("event_msg", new { type = "token_count", info = new { total_token_usage = new { total_tokens = 272000 }, last_token_usage = new { total_tokens = 270900 } } });
            Equal<UsageEvent?>(null, Parse(p, filler));
        });
        Check("H05 oversized child metadata must not turn into root usage", () =>
        {
            using var t = new Home();
            t.Write(Row("session_meta", new { id = "child", base_instructions = new string('x', LogScanner.MaxRecordBytes + 32), forked_from_id = "parent" }) + Context() + Count(900000, 9000));
            var r = new LogScanner(t.Cache).Scan(t.Root);
            Equal(0, r.Events.Count); Assert(r.Warnings > 0, "Incomplete accounting must be visible");
        });
        Check("H07 identical usage in independent session ids counts twice", () =>
        {
            using var t = new Home();
            t.Write(Row("session_meta", new { id = "a" }) + Context() + Count(1000, 100));
            File.WriteAllText(Path.Combine(t.Sessions, "other.jsonl"), Row("session_meta", new { id = "b" }) + Context() + Count(1000, 100));
            Equal(2, new LogScanner(t.Cache).Scan(t.Root).Events.Count);
        });
        Check("H08 case-insensitive custom model keys", () =>
        {
            using var t = new Home(); var file = Path.Combine(t.Root, "prices.json");
            File.WriteAllText(file, "{\"Mi-Modelo\":{\"Input\":2,\"Cached\":0.1,\"Output\":10}}");
            Equal(0.003m, PriceCatalog.Load(file).Cost(new(At, "mi-modelo", new(1000, 0, 100, 0, 1100))));
        });
        Check("H08 incomplete custom prices are rejected, not zero-filled", () =>
        {
            using var t = new Home(); var file = Path.Combine(t.Root, "prices.json");
            foreach (var json in new[] { "{\"x\":{\"Input\":2}}", "{\"x\":{\"input_per_million\":2,\"cached\":0.1,\"output\":10}}" })
            {
                File.WriteAllText(file, json); bool rejected = false;
                try { PriceCatalog.Load(file); } catch (Exception e) when (e is JsonException or InvalidOperationException) { rejected = true; }
                Assert(rejected, "Incomplete/misspelled tariff accepted");
            }
        });
        Check("H11 legacy schema cache is rebuilt after parser changes", () =>
        {
            using var t = new Home(); t.Write(Context() + Count(1000, 100));
            new LogScanner(t.Cache).Scan(t.Root);
            var path = Directory.GetFiles(t.Cache, "*.json").Single();
            var c = JsonSerializer.Deserialize<FileCache>(File.ReadAllText(path))!;
            c.Schema = 1; c.Events.Clear(); AtomicJson.Write(path, c);
            Equal(1, new LogScanner(t.Cache).Scan(t.Root).Events.Count);
        });
        Check("H12 oversized response text alone is not missing accounting", () =>
        {
            using var t = new Home();
            t.Write(Row("session_meta", new { id = "root" }) + Row("response_item", new { text = new string('x', LogScanner.MaxRecordBytes + 32) }) + Context() + Count(1000, 100));
            var r = new LogScanner(t.Cache).Scan(t.Root);
            Equal(1, r.Events.Count); Equal(0, r.Warnings);
        });
        Check("H13 complete stable EOF record without newline is counted once", () =>
        {
            using var t = new Home(); t.Write(Context() + Count(1000, 100).TrimEnd('\n'));
            var s = new LogScanner(t.Cache); s.Scan(t.Root);
            Equal(1, s.Scan(t.Root).Events.Count);
            File.AppendAllText(t.Rollout, "\n"); Equal(1, s.Scan(t.Root).Events.Count);
        });
        Check("C4 legacy relative quota reset is recognized", () =>
        {
            using var doc = JsonDocument.Parse("{\"primary\":{\"used_percent\":10,\"resets_in_seconds\":120}}");
            Equal(At.AddSeconds(120), QuotaParser.Parse(doc.RootElement, At, "test").Windows.Single().ResetAt);
        });
        await CheckAsync("H15 invalid authentication header is a recoverable validation error", async () =>
        {
            using var t = new Home();
            File.WriteAllText(Path.Combine(t.Root, "auth.json"), JsonSerializer.Serialize(new { tokens = new { access_token = "SYNTHETIC", account_id = "account\r\nINJECTED" } }));
            using var client = new CodexUsageClient(new NeverSend());
            bool rejected = false;
            try { await client.ReadAsync(t.Root, CancellationToken.None); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, "Header corruption was not rejected safely");
        });
        await Extended();
        Console.WriteLine($"RESULT: {passed} passed; {failed} failed.");
        return failed == 0 ? 0 : 1;
    }
    private static void Check(string name, Action action)
    {
        try { action(); Console.WriteLine("PASS " + name); passed++; }
        catch (Exception e) { Console.WriteLine($"FAIL {name}: {e.GetType().Name}: {e.Message}"); failed++; }
    }
    private static async Task CheckAsync(string name, Func<Task> action)
    {
        try { await action(); Console.WriteLine("PASS " + name); passed++; }
        catch (Exception e) { Console.WriteLine($"FAIL {name}: {e.GetType().Name}: {e.Message}"); failed++; }
    }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void NotNull(object? value) => Assert(value is not null, "Expected value");
    private static string Row(string type, object payload, DateTimeOffset? at = null) => JsonSerializer.Serialize(new { timestamp = (at ?? At).ToString("O"), type, payload }) + "\n";
    private static string Context() => Row("turn_context", new { model = "gpt-6.1-sol" });
    private static object Raw(long input, long output) => new { input_tokens = input, output_tokens = output, total_tokens = input + output };
    private static string Count(long input, long output, DateTimeOffset? at = null) => Row("event_msg", new { type = "token_count", info = new { total_token_usage = Raw(input, output) } }, at);
    private static UsageEvent? Parse(CodexParser p, string line) => p.Parse(Encoding.UTF8.GetBytes(line), out _);
    private sealed class Home : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "AIUsage-audit-" + Guid.NewGuid().ToString("N"));
        public string Sessions => Path.Combine(Root, "sessions");
        public string Rollout => Path.Combine(Sessions, "rollout.jsonl");
        public string Cache => Path.Combine(Root, "cache");
        public Home() => Directory.CreateDirectory(Sessions);
        public void Write(string content) => File.WriteAllText(Rollout, content, new UTF8Encoding(false));
        public void Dispose() { Directory.Delete(Root, true); }
    }
    private sealed class NeverSend : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new Exception("No request should be sent");
    }
}

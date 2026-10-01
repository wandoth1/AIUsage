using System.Text;
using System.Text.Json;
using AIUsage.Core;

// Independent reproductions from the second audit; all data is synthetic.
internal static partial class Program
{
    private static int passed, failed;
    private static readonly DateTimeOffset At = DateTimeOffset.UtcNow.AddMinutes(-5);
    private static int Main()
    {
        foreach (string subtype in new[] { "user_message", "item_completed" })
        {
            Test("G1 oversized " + subtype + " retains earlier and later usage", () =>
            {
                using var t = new Home(); t.Write(Meta() + Context() + Count(1000, 100) + Large(subtype) + Count(3000, 300));
                var scan = new LogScanner(t.Cache).Scan(t.Root);
                Equal(2, scan.Events.Count); Equal(3300L, scan.Events.Sum(e => e.Tokens.Total)); Equal(0, scan.Warnings);
                scan = new LogScanner(t.Cache).Scan(t.Root);
                Equal(3300L, scan.Events.Sum(e => e.Tokens.Total)); Equal(0, scan.Warnings);
            });
        }
        Test("G1 new appends survive a large conversation record", () =>
        {
            using var t = new Home(); t.Write(Meta() + Context() + Count(1000, 100) + Large("user_message"));
            var scanner = new LogScanner(t.Cache); scanner.Scan(t.Root);
            File.AppendAllText(t.Rollout, Count(3000, 300));
            Equal(3300L, scanner.Scan(t.Root).Events.Sum(e => e.Tokens.Total));
        });
        Test("G1 oversized token accounting still quarantines the file", () =>
        {
            using var t = new Home(); t.Write(Meta() + Context() + Count(1000, 100) + Large("token_count") + Count(3000, 300));
            var r = new LogScanner(t.Cache).Scan(t.Root); Equal(0, r.Events.Count); Require(r.Warnings > 0, "Missing warning");
        });
        Test("G1 oversized child metadata still quarantines the file", () =>
        {
            using var t = new Home(); t.Write(Row("session_meta", new { id = "child", forked_from_id = "parent", text = new string('x', LogScanner.MaxRecordBytes + 64) }) + Context() + Count(3000, 300));
            var r = new LogScanner(t.Cache).Scan(t.Root); Equal(0, r.Events.Count); Require(r.Warnings > 0, "Missing warning");
        });
        MoreCases();
        FolderCases();
        Console.WriteLine($"RESULT: {passed} passed; {failed} failed."); return failed == 0 ? 0 : 1;
    }
    private static string Row(string type, object payload) => JsonSerializer.Serialize(new { timestamp = At.ToString("O"), type, payload }) + "\n";
    private static string Meta() => Row("session_meta", new { id = "synthetic-root" });
    private static string Context() => Row("turn_context", new { model = "gpt-6.1-sol" });
    private static string Count(long input, long output) => Row("event_msg", new { type = "token_count", info = new { total_token_usage = new { input_tokens = input, output_tokens = output, total_tokens = input + output } } });
    private static string Large(string subtype) => Row("event_msg", new { type = subtype, message = "synthetic conversation only", item = new { type = "FunctionCallOutput", output = new string('x', LogScanner.MaxRecordBytes + 64) } });
    private static void Test(string name, Action test) { try { test(); passed++; Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; got {actual}"); }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private sealed class Home : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "AIUsage-followup-" + Guid.NewGuid().ToString("N"));
        public string Cache => Path.Combine(Root, "cache");
        public string Rollout => Path.Combine(Root, "sessions", "synthetic.jsonl");
        public Home() => Directory.CreateDirectory(Path.GetDirectoryName(Rollout)!);
        public void Write(string text) => File.WriteAllText(Rollout, text, new UTF8Encoding(false));
        public void Dispose() => Directory.Delete(Root, true);
    }
}

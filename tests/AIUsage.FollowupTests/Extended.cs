using System.Text;
using System.Text.Json;
using AIUsage.Core;

internal static partial class Program
{
    private static void MoreCases()
    {
        Test("G1 actual inline-image user message does not erase accounting", () =>
        {
            using var t = new Home();
            string image = "data:image/png;base64," + new string('A', LogScanner.MaxRecordBytes + 64);
            t.Write(Meta() + Context() + Count(1000, 100) + Row("event_msg", new { type = "user_message", message = "synthetic image", images = new[] { image } }) + Count(3000, 300));
            var r = new LogScanner(t.Cache).Scan(t.Root); Equal(3300L, r.Events.Sum(e => e.Tokens.Total)); Equal(0, r.Warnings);
        });
        Test("G1 actual paginated tool result does not erase accounting", () =>
        {
            using var t = new Home();
            t.Write(Meta() + Context() + Count(1000, 100) + Row("event_msg", new { type = "item_completed", thread_id = "synthetic-root", turn_id = "turn", item = new { type = "FunctionCallOutput", output = new string('x', LogScanner.MaxRecordBytes + 64) } }) + Count(3000, 300));
            Equal(3300L, new LogScanner(t.Cache).Scan(t.Root).Events.Sum(e => e.Tokens.Total));
        });
        Test("G1 other known conversation and tool completions preserve parser state", () =>
        {
            foreach (string type in new[] { "agent_message", "agent_reasoning", "agent_reasoning_raw_content", "mcp_tool_call_end", "web_search_end", "image_generation_end", "patch_apply_end" })
            {
                var p = new CodexParser(); p.Parse(Encoding.UTF8.GetBytes(Context()), out _);
                p.SkipOversized(Encoding.UTF8.GetBytes("{\"type\":\"event_msg\",\"payload\":{\"type\":\"" + type + "\",\"message\":\"partial"));
                Require(!p.State.AccountingBlocked, type + " incorrectly blocked"); Equal(0, p.Warnings); Equal("gpt-6.1-sol", p.State.Model);
            }
        });
        Test("G1 session, model, task and tier metadata stay conservative", () =>
        {
            foreach (string prefix in new[] {
                "{\"type\":\"session_meta\",\"payload\":{\"type\":\"user_message\",",
                "{\"type\":\"turn_context\",\"payload\":{\"type\":\"item_completed\",",
                "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",",
                "{\"type\":\"event_msg\",\"payload\":{\"type\":\"thread_settings_applied\"," })
            {
                var p = new CodexParser(); p.SkipOversized(Encoding.UTF8.GetBytes(prefix));
                Require(p.State.AccountingBlocked, "Accounting metadata bypassed quarantine"); Equal(1, p.Warnings);
            }
        });
        Test("G1 unknown or unreadable event subtypes remain quarantined", () =>
        {
            foreach (string prefix in new[] { "{\"type\":\"event_msg\",\"payload\":{\"type\":\"future_counter\",", "{\"type\":\"event_msg\",\"payload\":{\"type\":42,", "{\"type\":\"event_msg\",\"payload\":{\"message\":\"partial" })
            { var p = new CodexParser(); p.SkipOversized(Encoding.UTF8.GetBytes(prefix)); Require(p.State.AccountingBlocked, "Unknown event accepted"); }
        });
        Test("G1 nested fake payload types cannot impersonate message events", () =>
        {
            foreach (string prefix in new[] {
                "{\"type\":\"event_msg\",\"metadata\":{\"type\":\"user_message\"},\"payload\":{\"type\":\"token_count\",",
                "{\"type\":\"event_msg\",\"payload\":{\"item\":{\"type\":\"user_message\"},\"type\":\"token_count\"," })
            { var p = new CodexParser(); p.SkipOversized(Encoding.UTF8.GetBytes(prefix)); Require(p.State.AccountingBlocked, "Nested discriminator accepted"); }
        });
        Test("G1 BOM, whitespace and escaped JSON discriminator are supported", () =>
        {
            var p = new CodexParser();
            p.SkipOversized(Encoding.UTF8.GetBytes("\uFEFF { \"type\": \"event_msg\", \"payload\": { \"type\": \"user_\\u006dessage\", \"message\": \"partial"));
            Require(!p.State.AccountingBlocked, "Valid prefix rejected"); Equal(0, p.Warnings);
        });
        Test("G1 incomplete UTF8 content after a valid message header is harmless", () =>
        {
            var prefix = Encoding.UTF8.GetBytes("{\"type\":\"event_msg\",\"payload\":{\"type\":\"user_message\",\"message\":\"");
            var p = new CodexParser(); p.SkipOversized([.. prefix, 0xf0, 0x9f]);
            Require(!p.State.AccountingBlocked, "Message content parsed unnecessarily"); Equal(0, p.Warnings);
        });
        Test("G1 large EOF conversation is harmless before and after newline", () =>
        {
            using var t = new Home(); t.Write(Meta() + Context() + Count(1000, 100) + Large("user_message").TrimEnd('\n'));
            var s = new LogScanner(t.Cache); s.Scan(t.Root); var r = s.Scan(t.Root);
            Equal(1100L, r.Events.Sum(e => e.Tokens.Total)); Equal(0, r.Warnings);
            File.AppendAllText(t.Rollout, "\n" + Count(3000, 300)); Equal(3300L, s.Scan(t.Root).Events.Sum(e => e.Tokens.Total));
        });
        Test("G1 old quarantined caches rebuild and recover without editing logs", () =>
        {
            using var t = new Home(); t.Write(Meta() + Context() + Count(1000, 100) + Large("user_message") + Count(3000, 300));
            var r = new LogScanner(t.Cache).Scan(t.Root); Equal(3300L, r.Events.Sum(e => e.Tokens.Total));
            string path = Directory.GetFiles(t.Cache, "*.json").Single(); var old = JsonSerializer.Deserialize<FileCache>(File.ReadAllText(path))!;
            old.Schema = 3; old.State.AccountingBlocked = true; old.Events.Clear(); old.Warnings = 1; AtomicJson.Write(path, old);
            r = new LogScanner(t.Cache).Scan(t.Root); Equal(3300L, r.Events.Sum(e => e.Tokens.Total)); Equal(0, r.Warnings);
        });
        Test("G1 oversized private message contents are never cached", () =>
        {
            using var t = new Home(); const string sentinel = "SYNTHETIC_PRIVATE_IMAGE_OR_TEXT";
            t.Write(Meta() + Context() + Count(1000, 100) + Row("event_msg", new { type = "user_message", message = sentinel + new string('x', LogScanner.MaxRecordBytes + 64) }) + Count(3000, 300));
            new LogScanner(t.Cache).Scan(t.Root);
            Require(Directory.GetFiles(t.Cache, "*.json").All(p => !File.ReadAllText(p).Contains(sentinel)), "Private message cached");
        });
        Test("G4 Sol long-cache surcharge is qualified in both languages", () =>
        {
            var c = PriceCatalog.Load(); var e = new UsageEvent(At, "gpt-5.6-sol", new(300000, 100000, 1000, 0, 301000));
            foreach (string language in new[] { "en", "es" })
            { L10n.SetLanguage(language); var q = c.Quote(e); Equal<decimal?>(1.71m, q.Cost); Require(q.Note.Contains(L10n.T("LongCacheNote")), "Missing uncertainty qualification"); }
            L10n.SetLanguage("en");
        });
    }
}

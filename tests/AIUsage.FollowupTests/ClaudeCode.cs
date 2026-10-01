using System.Text;
using System.Text.Json;
using AIUsage.Core;

// Claude Code transcripts. Synthetic folders only: every scan receives a temporary home, and the
// CLAUDE_CONFIG_DIR check redirects to a temporary directory, so nothing reaches the real ~/.claude.
internal static partial class Program
{
    private static readonly PriceCatalog ClaudePrices = PriceCatalog.Load();
    private static void ClaudeCases()
    {
        Test("Claude: usage maps to AIUsage tokens and official list prices", () =>
        {
            using var t = new ClaudeHome();
            t.Write(@"projects\D--work\s1.jsonl", Claude("msg_a", "req_a", "claude-opus-5-5", 1000, 500, read: 10000, w5: 2000, w1: 3000));
            var e = Single(t.Scan());
            Equal(new Tokens(16000, 10000, 500, 0, 16500, 5000, 3000), e.Tokens);
            // 1000×$4 + 10000×$0.20 + 2000×$4×1.25 + 3000×$4×2 + 500×$20, per million tokens.
            Equal(0.05m, ClaudePrices.Cost(e));
        });
        Test("Claude: fast mode and US-only inference stack on Opus 5.5", () =>
        {
            using var t = new ClaudeHome();
            t.Write(@"projects\p\fast.jsonl", Claude("msg_f", "req_f", "claude-opus-5-5", 1000, 500, read: 10000, w5: 2000, w1: 3000, speed: "fast") +
                Claude("msg_g", "req_g", "claude-opus-5-5", 1000, 500, read: 10000, w5: 2000, w1: 3000, speed: "fast", geo: "us"));
            var events = t.Scan().Events;
            Equal(0.10m, ClaudePrices.Cost(events[0])); Equal(0.11m, ClaudePrices.Cost(events[1]));
            Require(ClaudePrices.Quote(events[1]).Note.Length > 0, "US-only inference is not disclosed");
        });
        Test("Claude: legacy aggregate cache writes count as 5-minute writes; dated ids resolve", () =>
        {
            using var t = new ClaudeHome();
            t.Write(@"projects\p\legacy.jsonl", Claude("msg_l", "req_l", "claude-sonnet-4-6", 100, 10, legacyWrite: 1000) +
                Claude("msg_h", "req_h", "claude-haiku-4-5-20251001", 1_000_000, 0));
            var events = t.Scan().Events;
            Equal(0.0042m, ClaudePrices.Cost(events[0])); Equal(1m, ClaudePrices.Cost(events[1]));
        });
        Test("Claude: replayed requests are counted once", () =>
        {
            using var t = new ClaudeHome();
            string first = Claude("msg_1", "req_1", "claude-sonnet-5", 100, 10);
            t.Write(@"projects\p\original.jsonl", first + Claude("msg_2", "req_2", "claude-sonnet-5", 200, 20));
            t.Write(@"projects\p\resumed.jsonl", first + Claude("msg_3", "req_3", "claude-sonnet-5", 300, 30));
            // A subagent sidechain replaying msg_2 under a new request id; the main-chain copy wins.
            t.Write(@"projects\p\s\subagents\agent-1.jsonl", Claude("msg_2", "req_x", "claude-sonnet-5", 999, 99, sidechain: true));
            var scan = t.Scan();
            Equal(3, scan.Events.Count); Equal(110L + 220 + 330, scan.Events.Sum(x => x.Tokens.Total)); Equal(3, scan.Files);
        });
        Test("Claude: on an exact duplicate the larger record wins", () =>
        {
            using var t = new ClaudeHome();
            t.Write(@"projects\p\a.jsonl", Claude("msg_s", "req_s", "claude-opus-5", 100, 1) + Claude("msg_s", "req_s", "claude-opus-5", 100, 50));
            Equal(150L, Single(t.Scan()).Tokens.Total);
        });
        Test("Claude: advisor iterations are billed under the advisor model", () =>
        {
            using var t = new ClaudeHome();
            var advisor = new object[]
            {
                new { type = "message", model = (string?)null, input_tokens = 5, output_tokens = 5 },
                new { type = "advisor_message", model = "claude-fable-5-1", input_tokens = 1000, output_tokens = 100 }
            };
            t.Write(@"projects\p\adv.jsonl", Claude("msg_v", "req_v", "claude-sonnet-5-5", 100, 10, iterations: advisor));
            var events = t.Scan().Events;
            Equal(2, events.Count); Equal("claude-fable-5-1", events[1].Model); Equal(1100L, events[1].Tokens.Total);
        });
        Test("Claude: foreign or local-only records are ignored", () =>
        {
            using var t = new ClaudeHome();
            t.Write(@"projects\p\bad.jsonl",
                Claude("msg_1", "req_1", "<synthetic>", 0, 0) +
                Claude("msg_2", "req_2", "claude-opus-5", 100, 10, version: "unknown") +
                Claude("msg_3", "req_3", "claude-opus-5", 100, 10, speed: "turbo") +
                Claude("", "req_4", "claude-opus-5", 100, 10) +
                Claude("msg_5", "req_5", "claude-opus-5", 100, 10).Replace("\"cwd\":\"C:\\\\synthetic\"", "\"cwd\":null") +
                JsonSerializer.Serialize(new { type = "user", timestamp = At.ToString("O"), message = new { role = "user", content = "input_tokens are mentioned here" } }) + "\n" +
                Claude("msg_ok", "req_ok", "claude-opus-5", 100, 10));
            var scan = t.Scan();
            Equal(1, scan.Events.Count); Equal(110L, scan.Events[0].Tokens.Total); Equal(0, scan.Warnings);
        });
        Test("Claude: only projects transcripts are read, never credentials or history", () =>
        {
            using var t = new ClaudeHome();
            string record = Claude("msg_x", "req_x", "claude-opus-5", 100, 10);
            t.Write(".credentials.json", record); t.Write("history.jsonl", record); t.Write(@"todos\t.jsonl", record);
            t.Write(@"projects\p\notes.txt", record);
            var scan = t.Scan(); Equal(0, scan.Events.Count); Equal(0, scan.Files);
        });
        Test("Claude: appends are read incrementally and the cache holds no content or raw ids", () =>
        {
            using var t = new ClaudeHome();
            string file = t.Write(@"projects\p\live.jsonl", Claude("msg_c1", "req_c1", "claude-opus-5", 100, 10, text: "SYNTHETIC-PROMPT-TEXT"));
            var scanner = new ClaudeLogScanner(t.Cache);
            Equal(110L, scanner.Scan(t.Root).Events.Sum(x => x.Tokens.Total));
            string partial = Claude("msg_c2", "req_c2", "claude-opus-5", 200, 20);
            File.AppendAllText(file, partial[..^1]); // No newline yet: an active write is not counted.
            Equal(110L, scanner.Scan(t.Root).Events.Sum(x => x.Tokens.Total));
            File.AppendAllText(file, "\n");
            Equal(330L, scanner.Scan(t.Root).Events.Sum(x => x.Tokens.Total));
            var fresh = new ClaudeLogScanner(t.Cache);
            Equal(330L, fresh.Scan(t.Root).Events.Sum(x => x.Tokens.Total)); Equal(1, fresh.PersistentCacheHits);
            string cached = string.Concat(Directory.GetFiles(t.Cache).Select(File.ReadAllText));
            Require(!cached.Contains("SYNTHETIC-PROMPT-TEXT") && !cached.Contains("msg_c1") && !cached.Contains("req_c1") && !cached.Contains("synthetic-session"), "Cache retains content or raw ids");
        });
        Test("Claude: a rewritten transcript is read again from the start", () =>
        {
            using var t = new ClaudeHome();
            string file = t.Write(@"projects\p\r.jsonl", Claude("msg_r1", "req_r1", "claude-opus-5", 100, 10) + Claude("msg_r2", "req_r2", "claude-opus-5", 100, 10));
            var scanner = new ClaudeLogScanner(t.Cache); Equal(2, scanner.Scan(t.Root).Events.Count);
            File.WriteAllText(file, Claude("msg_r3", "req_r3", "claude-opus-5", 1, 1), new UTF8Encoding(false));
            var events = scanner.Scan(t.Root).Events; Equal(1, events.Count); Equal(2L, events[0].Tokens.Total);
        });
        Test("Claude: folder resolution honours CLAUDE_CONFIG_DIR", () =>
        {
            using var t = new ClaudeHome();
            string? old = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            try { Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", t.Root); Equal(Path.GetFullPath(t.Root), ClaudeLogScanner.ResolveHome()); }
            finally { Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", old); }
        });
        Test("Claude: settings keep the source opt-in", () =>
        {
            Require(!new AppSettings().ClaudeCode, "Claude Code must be off by default");
            using var t = new ClaudeHome(); string path = Path.Combine(t.Root, "settings.json");
            AtomicJson.Write(path, new AppSettings { ClaudeCode = true }); Require(AppSettings.Load(path).ClaudeCode, "Setting not persisted");
        });
    }
    private static UsageEvent Single(ScanResult scan) { Equal(1, scan.Events.Count); return scan.Events[0]; }
    private static string Claude(string id, string request, string model, long input, long output, long read = 0, long w5 = 0, long w1 = 0,
        long? legacyWrite = null, string? speed = "standard", string? geo = null, bool sidechain = false, string version = "2.1.0",
        object? iterations = null, string text = "synthetic answer")
    {
        var usage = new Dictionary<string, object?> { ["input_tokens"] = input, ["output_tokens"] = output, ["cache_read_input_tokens"] = read, ["service_tier"] = "standard", ["inference_geo"] = geo ?? "not_available" };
        if (legacyWrite is { } write) usage["cache_creation_input_tokens"] = write;
        else { usage["cache_creation_input_tokens"] = w5 + w1; usage["cache_creation"] = new { ephemeral_5m_input_tokens = w5, ephemeral_1h_input_tokens = w1 }; }
        if (speed is not null) usage["speed"] = speed;
        if (iterations is not null) usage["iterations"] = iterations;
        var row = new Dictionary<string, object?>
        {
            ["parentUuid"] = null, ["isSidechain"] = sidechain, ["cwd"] = @"C:\synthetic", ["sessionId"] = "synthetic-session", ["version"] = version,
            ["message"] = new Dictionary<string, object?> { ["id"] = id, ["type"] = "message", ["role"] = "assistant", ["model"] = model,
                ["content"] = new[] { new { type = "text", text } }, ["usage"] = usage },
            ["requestId"] = request, ["type"] = "assistant", ["uuid"] = Guid.NewGuid().ToString(), ["timestamp"] = At.ToString("O")
        };
        return JsonSerializer.Serialize(row) + "\n";
    }
    private sealed class ClaudeHome : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "AIUsage-claude-" + Guid.NewGuid().ToString("N"));
        public string Cache => Path.Combine(Root, "aiusage-cache");
        public ClaudeHome() => Directory.CreateDirectory(Root);
        public string Write(string relative, string text)
        {
            string path = Path.Combine(Root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false)); return path;
        }
        public ScanResult Scan() => new ClaudeLogScanner(Cache).Scan(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }
}

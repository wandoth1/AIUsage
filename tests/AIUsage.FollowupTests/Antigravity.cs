using System.Text;
using System.Text.Json;
using AIUsage.Core;

// Antigravity plan quota from the Antigravity CLI's documented status line input, and Google's Gemini prices.
// Synthetic JSON and temporary folders only.
internal static partial class Program
{
    private static void AntigravityCases()
    {
        var now = new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
        Test("Antigravity: quota is saved and shown; e-mail and everything else are discarded", () =>
        {
            using var t = new ClaudeHome();
            Equal("Gemini 3.5 Flash (High) · quota 75%", AntigravityStatusLine.Run(AgyInput(true), t.Root, now));
            string saved = File.ReadAllText(Path.Combine(t.Root, AntigravityStatusLine.FileName));
            Require(!saved.Contains("synthetic@example.com") && !saved.Contains("synthetic-project") && !saved.Contains("conv-x") && !saved.Contains("88244"), "Unrelated session data saved");
            var q = AntigravityStatusLine.Load(t.Root)!;
            Equal(AntigravityStatusLine.Source, q.Source); Equal(2, q.Windows.Count);
            Equal("antigravity:gemini-3.8-flash", q.Windows[0].Id); Equal(6.2, q.Windows[0].UsedPercent);
            Equal(new DateTimeOffset(2026, 10, 18, 2, 0, 0, TimeSpan.Zero), q.Windows[0].ResetAt);
            Equal(75.0, q.Windows[1].UsedPercent); Equal(now.AddSeconds(3600), q.Windows[1].ResetAt); Equal("Antigravity · gemini-pro", q.Windows[1].Name);
        });
        Test("Antigravity: input without quota keeps the last snapshot", () =>
        {
            using var t = new ClaudeHome();
            AntigravityStatusLine.Run(AgyInput(true), t.Root, now);
            Equal("Gemini 3.5 Flash (High)", AntigravityStatusLine.Run(AgyInput(false), t.Root, now.AddMinutes(5)));
            Equal(now, AntigravityStatusLine.Load(t.Root)!.At);
        });
        Test("Antigravity: malformed, hostile or oversized quota is ignored", () =>
        {
            using var t = new ClaudeHome();
            var quota = new Dictionary<string, object> { ["bad bucket"] = new { remaining_fraction = 0.5 }, ["x\u001b]0;"] = new { remaining_fraction = 0.5 },
                ["over"] = new { remaining_fraction = 1.5 }, ["text"] = new { remaining_fraction = "half" }, ["ok"] = new { remaining_fraction = 0.1 } };
            for (int i = 0; i < 30; i++) quota["bucket-" + i] = new { remaining_fraction = 0.5 };
            string json = JsonSerializer.Serialize(new { model = new { display_name = "Gemini\u202E" }, quota });
            Equal("Gemini · quota 90%", AntigravityStatusLine.Run(new MemoryStream(Encoding.UTF8.GetBytes(json)), t.Root, now));
            var q = AntigravityStatusLine.Load(t.Root)!; Equal(AntigravityStatusLine.MaxWindows, q.Windows.Count); Equal("antigravity:ok", q.Windows[0].Id);
            Equal("", AntigravityStatusLine.Run(new MemoryStream("{not json"u8.ToArray()), t.Root, now));
            Equal("", AntigravityStatusLine.Run(new MemoryStream(new byte[ClaudeStatusLine.MaxInputBytes + 10]), t.Root, now));
        });
        Test("Antigravity: a tampered snapshot is normalised or rejected", () =>
        {
            using var t = new ClaudeHome(); string file = Path.Combine(t.Root, AntigravityStatusLine.FileName);
            AtomicJson.Write(file, new QuotaSnapshot(now, AntigravityStatusLine.Source, "plan", [new("INJECTED", 40, null, 99, "antigravity:gemini-pro")], "credits", 1, "account"));
            var q = AntigravityStatusLine.Load(t.Root)!;
            Equal("Antigravity · gemini-pro", q.Windows[0].Name); Equal(null, q.Windows[0].Seconds); Equal(null, q.Plan); Equal(null, q.AccountKey);
            foreach (var bad in new LimitWindow[] { new("x", 5, null, null, "claude:five_hour"), new("x", 140, null, null, "antigravity:a"), new("x", 5, null, null, "antigravity:Bad Id") })
            {
                AtomicJson.Write(file, new QuotaSnapshot(now, AntigravityStatusLine.Source, null, [bad])); Equal(null, AntigravityStatusLine.Load(t.Root));
            }
        });
        Test("Antigravity: settings entry keeps Antigravity's own line and needs a shell-safe path", () =>
        {
            var line = JsonDocument.Parse(AntigravityStatusLine.SettingsSnippet(@"C:\Users\someone\AIUsage-1.7.0-win-x64\AIUsage.exe")!).RootElement.GetProperty("statusLine");
            Equal("C:/Users/someone/AIUsage-1.7.0-win-x64/AIUsage.exe --antigravity-statusline", line.GetProperty("command").GetString());
            Require(line.GetProperty("stack_with_default").GetBoolean() && line.GetProperty("type").GetString() == "command", "Unexpected entry");
            Equal(null, AntigravityStatusLine.SettingsSnippet(@"C:\My Apps\AIUsage.exe"));
            Require(AntigravityStatusLine.SettingsFile().EndsWith(@"\.gemini\antigravity-cli\settings.json", StringComparison.OrdinalIgnoreCase), "Unexpected settings file");
            Require(!new AppSettings().Antigravity, "Antigravity limits must be off by default");
        });
        Test("Gemini: Google's prices, including 3.1 Pro above 200,000 prompt tokens", () =>
        {
            Equal(2m, ClaudePrices.Cost(new UsageEvent(now, "gemini-4-argon", new Tokens(1_000_000, 0, 0, 0, 1_000_000))));
            Equal(0.0045m, ClaudePrices.Cost(new UsageEvent(now, "gemini-3.8-flash", new Tokens(1000, 0, 1000, 0, 2000))));       // $0.75 + $3.75 per million
            Equal(0.212m, ClaudePrices.Cost(new UsageEvent(now, "gemini-3.1-pro", new Tokens(100_000, 0, 1000, 0, 101_000))));     // 100K×$2 + 1K×$12
            Equal(1.218m, ClaudePrices.Cost(new UsageEvent(now, "gemini-3.1-pro", new Tokens(300_000, 0, 1000, 0, 301_000))));     // >200K: 300K×$4 + 1K×$18
        });
    }
    // Shape documented at https://antigravity.google/docs/cli/statusline/ (values are synthetic).
    private static MemoryStream AgyInput(bool quota)
    {
        var row = new Dictionary<string, object>
        {
            ["cwd"] = @"C:\synthetic-project", ["session_id"] = "conv-x", ["conversation_id"] = "conv-x", ["email"] = "synthetic@example.com",
            ["model"] = new { id = "Gemini 3.5 Flash (High)", display_name = "Gemini 3.5 Flash (High)" }, ["product"] = "antigravity", ["agent_state"] = "idle",
            ["context_window"] = new { total_input_tokens = 88244, total_output_tokens = 1200, current_usage = new { input_tokens = 63382, output_tokens = 1200 } }
        };
        if (quota) row["quota"] = new Dictionary<string, object>
        {
            ["gemini-3.8-flash"] = new { remaining_fraction = 0.9378, reset_time = "2026-10-18T02:00:00Z", reset_in_seconds = 61200 },
            ["gemini-pro"] = new { remaining_fraction = 0.25, reset_time = "soon", reset_in_seconds = 3600 }
        };
        return new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(row)));
    }
}

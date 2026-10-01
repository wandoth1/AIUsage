using System.Text;
using System.Text.Json;
using AIUsage.Core;

// Claude plan limits from Claude Code's documented status line input. Synthetic JSON and temporary folders only.
internal static partial class Program
{
    private static void ClaudeStatusCases()
    {
        var now = new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);
        Test("Status line: rate limits are saved and shown, nothing else is kept", () =>
        {
            using var t = new ClaudeHome();
            string text = ClaudeStatusLine.Run(StatusInput(true), t.Root, now);
            Equal("Opus 5.5 · 5h 24% · 7d 41%", text);
            string saved = File.ReadAllText(Path.Combine(t.Root, ClaudeStatusLine.FileName));
            Require(!saved.Contains("synthetic-project") && !saved.Contains("transcript") && !saved.Contains("session-x") && !saved.Contains("1.23"), "Unrelated session data saved");
            var q = ClaudeStatusLine.Load(t.Root)!;
            Equal(2, q.Windows.Count); Equal(ClaudeStatusLine.Source, q.Source); Equal(now, q.At);
            Equal(23.5, q.Windows[0].UsedPercent); Equal(DateTimeOffset.FromUnixTimeSeconds(1790000000), q.Windows[0].ResetAt);
            Equal("claude:seven_day", q.Windows[1].Id); Equal(604800L, q.Windows[1].Seconds);
        });
        Test("Status line: input without limits keeps the last snapshot", () =>
        {
            using var t = new ClaudeHome();
            ClaudeStatusLine.Run(StatusInput(true), t.Root, now);
            Equal("Opus 5.5", ClaudeStatusLine.Run(StatusInput(false), t.Root, now.AddMinutes(5)));
            Equal(now, ClaudeStatusLine.Load(t.Root)!.At);
        });
        Test("Status line: malformed, oversized or hostile input is harmless", () =>
        {
            using var t = new ClaudeHome();
            Equal("", ClaudeStatusLine.Run(new MemoryStream("{not json"u8.ToArray()), t.Root, now));
            Equal("", ClaudeStatusLine.Run(new MemoryStream(new byte[ClaudeStatusLine.MaxInputBytes + 10]), t.Root, now));
            Equal(null, ClaudeStatusLine.Load(t.Root));
            string hostile = JsonSerializer.Serialize(new { model = new { display_name = "Opus\u001b]0;x\u0007\n" }, rate_limits = new { five_hour = new { used_percentage = "50" }, seven_day = new { used_percentage = -1 } } });
            Equal("Opus]0;x", ClaudeStatusLine.Run(new MemoryStream(Encoding.UTF8.GetBytes(hostile)), t.Root, now));
            Equal(null, ClaudeStatusLine.Load(t.Root));
        });
        Test("Status line: a tampered snapshot is ignored", () =>
        {
            using var t = new ClaudeHome();
            AtomicJson.Write(Path.Combine(t.Root, ClaudeStatusLine.FileName), new QuotaSnapshot(now, ClaudeStatusLine.Source, null, [new("x", 1, null, null, "codex:1")]));
            Equal(null, ClaudeStatusLine.Load(t.Root));
        });
        Test("Status line: settings snippet is valid JSON with forward slashes", () =>
        {
            var command = JsonDocument.Parse(ClaudeStatusLine.SettingsSnippet(@"C:\Users\someone\Apps\AIUsage\AIUsage.exe")).RootElement.GetProperty("statusLine");
            Equal("command", command.GetProperty("type").GetString());
            Equal("C:/Users/someone/Apps/AIUsage/AIUsage.exe --claude-statusline", command.GetProperty("command").GetString());
            Equal("\"C:/My Apps/AIUsage.exe\" --claude-statusline", JsonDocument.Parse(ClaudeStatusLine.SettingsSnippet(@"C:\My Apps\AIUsage.exe")).RootElement.GetProperty("statusLine").GetProperty("command").GetString());
        });
    }
    // Shape documented at https://code.claude.com/docs/en/statusline (values are synthetic).
    private static MemoryStream StatusInput(bool limits)
    {
        var row = new Dictionary<string, object>
        {
            ["session_id"] = "session-x", ["transcript_path"] = @"C:\synthetic\transcript.jsonl", ["cwd"] = @"C:\synthetic-project",
            ["model"] = new { id = "claude-opus-5-5", display_name = "Opus 5.5" }, ["cost"] = new { total_cost_usd = 1.23 }
        };
        if (limits) row["rate_limits"] = new { five_hour = new { used_percentage = 23.5, resets_at = 1790000000 }, seven_day = new { used_percentage = 41.2, resets_at = 1790500000 } };
        return new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(row)));
    }
}

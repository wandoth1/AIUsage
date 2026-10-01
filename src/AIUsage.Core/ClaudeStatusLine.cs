using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AIUsage.Core;

/// Claude plan limits through Claude Code's documented status line: Claude Code runs a command the user
/// configures (statusLine in its settings) and passes session JSON on stdin, which for Pro/Max includes
/// rate_limits (https://code.claude.com/docs/en/statusline). AIUsage keeps only those percentages and reset
/// times. No credentials, sign-in or Anthropic requests are involved; everything else on stdin is discarded.
public static class ClaudeStatusLine
{
    public const string Argument = "--claude-statusline";
    public const string FileName = "claude-limits.json";
    public const string Source = "Claude Code status line";
    public const int MaxInputBytes = 1024 * 1024;

    /// Handles one status line refresh: saves any rate limits and returns the text Claude Code displays.
    public static string Run(Stream input, string dataDirectory, DateTimeOffset now)
    {
        var bytes = new byte[MaxInputBytes + 1];
        int n = input.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        if (n > MaxInputBytes) return "";
        JsonDocument document;
        try { document = JsonDocument.Parse(bytes.AsMemory(0, n), new JsonDocumentOptions { MaxDepth = 64 }); }
        catch (JsonException) { return ""; }
        using (document)
        {
            var root = document.RootElement;
            var snapshot = Parse(root, now);
            // Absent rate_limits (API-key users, before the first response) keeps the last saved snapshot.
            if (snapshot is not null)
            {
                try { AtomicJson.Write(Path.Combine(dataDirectory, FileName), snapshot); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            return Render(root, snapshot);
        }
    }
    internal static QuotaSnapshot? Parse(JsonElement root, DateTimeOffset now)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("rate_limits", out var limits) || limits.ValueKind != JsonValueKind.Object) return null;
        var windows = new List<LimitWindow>();
        foreach (string key in Windows)
        {
            if (!limits.TryGetProperty(key, out var window) || window.ValueKind != JsonValueKind.Object ||
                !window.TryGetProperty("used_percentage", out var used) || used.ValueKind != JsonValueKind.Number || !used.TryGetDouble(out double percent)) continue;
            DateTimeOffset? reset = null;
            if (window.TryGetProperty("resets_at", out var at) && at.ValueKind == JsonValueKind.Number && at.TryGetInt64(out long epoch) &&
                epoch is > 0 and < 253402300799) reset = DateTimeOffset.FromUnixTimeSeconds(epoch);
            if (Window(key, percent, reset) is { } w) windows.Add(w);
        }
        return windows.Count == 0 ? null : new QuotaSnapshot(now, Source, null, windows);
    }
    // The only windows Claude Code documents. Names and durations derive from the id, never from stored text.
    private static readonly string[] Windows = ["five_hour", "seven_day", "spend_limit"];
    private static LimitWindow? Window(string key, double percent, DateTimeOffset? reset)
    {
        // five_hour/seven_day run 0-100; a gateway spend limit can exceed 100 once it is overspent.
        double maximum = key == "spend_limit" ? 1000 : 100;
        if (!double.IsFinite(percent) || percent < 0 || percent > maximum) return null;
        return key switch
        {
            "five_hour" => new("Claude · Session", percent, reset, 18000, "claude:five_hour"),
            "seven_day" => new("Claude · Weekly", percent, reset, 604800, "claude:seven_day"),
            "spend_limit" => new("Claude · Spend limit", percent, reset, null, "claude:spend_limit"),
            _ => null
        };
    }
    private static string Render(JsonElement root, QuotaSnapshot? snapshot)
    {
        var parts = new List<string>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object &&
            model.TryGetProperty("display_name", out var display) && display.ValueKind == JsonValueKind.String)
            parts.Add(Clean(display.GetString()!));
        foreach (var w in snapshot?.Windows ?? [])
        {
            string label = w.Id switch { "claude:five_hour" => "5h", "claude:seven_day" => "7d", _ => "spend" };
            parts.Add(label + " " + Math.Round(w.UsedPercent).ToString("0", CultureInfo.InvariantCulture) + "%");
        }
        return string.Join(" · ", parts);
    }
    // Status line text is rendered by a terminal: drop control characters (ANSI/OSC introducers), Unicode
    // format characters (bidirectional overrides, invisible marks) and line/paragraph separators.
    public static string Clean(string text)
    {
        var b = new StringBuilder();
        foreach (char c in text.Take(80))
            if (char.GetUnicodeCategory(c) is not (UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or
                UnicodeCategory.ParagraphSeparator or UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned)) b.Append(c);
        return b.ToString();
    }
    /// The last snapshot written by the status line command, rebuilt from known windows only; null when unavailable.
    public static QuotaSnapshot? Load(string dataDirectory)
    {
        try
        {
            string path = LocalPaths.Require(Path.Combine(dataDirectory, FileName));
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(LocalStorage.ReadText(path, 65536));
            var stored = document.RootElement.Deserialize<QuotaSnapshot>();
            if (stored is not { Windows: not null, Source: Source } || stored.Windows.Count is 0 or > 3 || stored.At > DateTimeOffset.Now.AddDays(1)) return null;
            var windows = new List<LimitWindow>();
            foreach (var w in stored.Windows)
            {
                if (w?.Id is not { } id || !id.StartsWith("claude:", StringComparison.Ordinal) || windows.Any(x => x.Id == id)) return null;
                if (Window(id["claude:".Length..], w.UsedPercent, w.ResetAt) is not { } known) return null;
                windows.Add(known);
            }
            return new QuotaSnapshot(stored.At, Source, null, windows);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException) { return null; }
    }
    /// The statusLine entry for Claude Code's settings.json, or null when the executable path cannot be written
    /// safely. Claude Code runs the command through Git Bash or PowerShell; the path is emitted unquoted, so it
    /// must contain only characters that neither shell interprets: letters, digits and . _ - / after "X:/".
    public static string? SettingsSnippet(string executable)
    {
        string path = executable.Replace('\\', '/');
        if (!IsShellSafePath(path)) return null;
        return "{\n  \"statusLine\": {\n    \"type\": \"command\",\n    \"command\": " + JsonSerializer.Serialize(path + " " + Argument) + "\n  }\n}";
    }
    internal static bool IsShellSafePath(string path) =>
        path.Length is > 3 and < 260 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '/' &&
        path[3..].All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '/') && !path.Contains("//", StringComparison.Ordinal);
}

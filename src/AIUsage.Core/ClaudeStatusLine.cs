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
        foreach (var (key, name, seconds) in new (string, string, long?)[] { ("five_hour", "Session", 18000), ("seven_day", "Weekly", 604800), ("spend_limit", "Spend limit", null) })
        {
            if (!limits.TryGetProperty(key, out var window) || window.ValueKind != JsonValueKind.Object ||
                !window.TryGetProperty("used_percentage", out var used) || used.ValueKind != JsonValueKind.Number ||
                !used.TryGetDouble(out double percent) || !double.IsFinite(percent) || percent < 0 || percent > 100_000) continue;
            DateTimeOffset? reset = null;
            if (window.TryGetProperty("resets_at", out var at) && at.ValueKind == JsonValueKind.Number && at.TryGetInt64(out long epoch) &&
                epoch is > 0 and < 253402300799) reset = DateTimeOffset.FromUnixTimeSeconds(epoch);
            windows.Add(new("Claude · " + name, percent, reset, seconds, "claude:" + key));
        }
        return windows.Count == 0 ? null : new QuotaSnapshot(now, Source, null, windows);
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
    // Status line text is rendered by a terminal: never echo control characters from the input.
    private static string Clean(string text)
    {
        var b = new StringBuilder();
        foreach (char c in text.Take(80)) if (!char.IsControl(c)) b.Append(c);
        return b.ToString();
    }
    /// The last snapshot written by the status line command, or null when none is available.
    public static QuotaSnapshot? Load(string dataDirectory)
    {
        try
        {
            string path = LocalPaths.Require(Path.Combine(dataDirectory, FileName));
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(LocalStorage.ReadText(path, 65536));
            var snapshot = document.RootElement.Deserialize<QuotaSnapshot>();
            if (snapshot is not { Windows: not null, Source: Source } || snapshot.Windows.Count is 0 or > 3 ||
                snapshot.Windows.Any(w => w is null || w.Name is null || w.Id is null || !w.Id.StartsWith("claude:", StringComparison.Ordinal))) return null;
            return snapshot;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException) { return null; }
    }
    /// The statusLine entry for Claude Code's settings.json. Forward slashes: Claude Code may run it through Git Bash.
    public static string SettingsSnippet(string executable)
    {
        string path = executable.Replace('\\', '/');
        string command = (path.Any(char.IsWhiteSpace) ? "\"" + path + "\"" : path) + " " + Argument;
        return "{\n  \"statusLine\": {\n    \"type\": \"command\",\n    \"command\": " + JsonSerializer.Serialize(command) + "\n  }\n}";
    }
}

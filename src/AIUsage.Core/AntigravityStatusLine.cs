using System.Globalization;
using System.Text.Json;

namespace AIUsage.Core;

/// Antigravity plan quota through the Antigravity CLI's documented status line: the CLI runs a command the user
/// configures (statusLine in ~/.gemini/antigravity-cli/settings.json) and passes session JSON on stdin, including a
/// quota object keyed by model/bucket with remaining_fraction and reset_time (https://antigravity.google/docs/cli/statusline/).
/// AIUsage keeps only those quota windows. No Google credentials, sign-in or Google requests are involved; everything
/// else on stdin (e-mail address, working directory, conversation id, tokens) is discarded.
public static class AntigravityStatusLine
{
    public const string Argument = "--antigravity-statusline";
    public const string FileName = "antigravity-limits.json";
    public const string Source = "Antigravity CLI status line";
    public const int MaxWindows = 16;

    /// Handles one status line refresh: saves any quota and returns the text the Antigravity CLI displays.
    public static string Run(Stream input, string dataDirectory, DateTimeOffset now)
    {
        var bytes = new byte[ClaudeStatusLine.MaxInputBytes + 1];
        int n = input.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        if (n > ClaudeStatusLine.MaxInputBytes) return "";
        JsonDocument document;
        try { document = JsonDocument.Parse(bytes.AsMemory(0, n), new JsonDocumentOptions { MaxDepth = 64 }); }
        catch (JsonException) { return ""; }
        using (document)
        {
            var root = document.RootElement;
            var snapshot = Parse(root, now);
            // Absent quota (before the first response) keeps the last saved snapshot.
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
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("quota", out var quota) || quota.ValueKind != JsonValueKind.Object) return null;
        var windows = new List<LimitWindow>();
        foreach (var bucket in quota.EnumerateObject())
        {
            if (windows.Count == MaxWindows) break;
            var value = bucket.Value;
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("remaining_fraction", out var remaining) ||
                remaining.ValueKind != JsonValueKind.Number || !remaining.TryGetDouble(out double fraction)) continue;
            DateTimeOffset? reset = null;
            if (value.TryGetProperty("reset_time", out var time) && time.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(time.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)) reset = parsed;
            else if (value.TryGetProperty("reset_in_seconds", out var seconds) && seconds.ValueKind == JsonValueKind.Number &&
                seconds.TryGetDouble(out double s) && s is >= 0 and < 315360000) reset = now.AddSeconds(s);
            if (Window(bucket.Name, (1 - fraction) * 100, reset) is { } w && windows.All(x => x.Id != w.Id)) windows.Add(w);
        }
        return windows.Count == 0 ? null : new QuotaSnapshot(now, Source, null, windows);
    }
    // Bucket ids are shown as given; names and identity derive from the id only, never from other stored text.
    private static LimitWindow? Window(string bucket, double usedPercent, DateTimeOffset? reset)
    {
        string id = bucket.Trim().ToLowerInvariant();
        if (id.Length is 0 or > 64 || !id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.' or '_') ||
            !double.IsFinite(usedPercent) || usedPercent < -0.0001 || usedPercent > 100.0001) return null;
        return new("Antigravity · " + id, Math.Round(Math.Clamp(usedPercent, 0, 100), 1), reset, null, "antigravity:" + id);
    }
    private static string Render(JsonElement root, QuotaSnapshot? snapshot)
    {
        var parts = new List<string>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object &&
            model.TryGetProperty("display_name", out var display) && display.ValueKind == JsonValueKind.String)
            parts.Add(ClaudeStatusLine.Clean(display.GetString()!));
        if (snapshot is { Windows.Count: > 0 })
            parts.Add("quota " + Math.Round(snapshot.Windows.Max(w => w.UsedPercent)).ToString("0", CultureInfo.InvariantCulture) + "%");
        return string.Join(" · ", parts);
    }
    /// The last snapshot written by the status line command, rebuilt from validated windows; null when unavailable.
    public static QuotaSnapshot? Load(string dataDirectory)
    {
        try
        {
            string path = LocalPaths.Require(Path.Combine(dataDirectory, FileName));
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(LocalStorage.ReadText(path, 65536));
            var stored = document.RootElement.Deserialize<QuotaSnapshot>();
            if (stored is not { Windows: not null, Source: Source } || stored.Windows.Count is 0 or > MaxWindows || stored.At > DateTimeOffset.Now.AddDays(1)) return null;
            var windows = new List<LimitWindow>();
            foreach (var w in stored.Windows)
            {
                if (w?.Id is not { } id || !id.StartsWith("antigravity:", StringComparison.Ordinal) || windows.Any(x => x.Id == id)) return null;
                if (Window(id["antigravity:".Length..], w.UsedPercent, w.ResetAt) is not { } known || known.Id != id) return null;
                windows.Add(known);
            }
            return new QuotaSnapshot(stored.At, Source, null, windows);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException) { return null; }
    }
    /// The statusLine entry for the Antigravity CLI's settings.json, or null when the path is not shell-safe.
    /// stack_with_default keeps Antigravity's own status line and shows AIUsage's line below it.
    public static string? SettingsSnippet(string executable)
    {
        string path = executable.Replace('\\', '/');
        if (!ClaudeStatusLine.IsShellSafePath(path)) return null;
        return "{\n  \"statusLine\": {\n    \"type\": \"command\",\n    \"command\": " + JsonSerializer.Serialize(path + " " + Argument) + ",\n    \"stack_with_default\": true\n  }\n}";
    }
    /// The Antigravity CLI's user settings file.
    public static string SettingsFile() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "settings.json");
}

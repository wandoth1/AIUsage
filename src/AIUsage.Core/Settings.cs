using static AIUsage.Core.L10n;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIUsage.Core;

public sealed class AppSettings
{
    [JsonIgnore] public bool MigrationSaveFailed { get; private set; }
    public string CodexHome { get; set; } = "";
    public int RefreshSeconds { get; set; } = 60;
    public bool LightTheme { get; set; }
    public string Language { get; set; } = "auto";
    // Opt-in: Claude Code transcripts are a second local source and are never read unless enabled.
    public bool ClaudeCode { get; set; }
    public string ResolveHome()
    {
        string path = string.IsNullOrWhiteSpace(CodexHome) ? Environment.GetEnvironmentVariable("CODEX_HOME") ?? "" : CodexHome;
        if (string.IsNullOrWhiteSpace(path)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        return ExpandFolder(path);
    }
    internal static string ExpandFolder(string path)
    {
        path = path.Trim().Trim('"');
        if (path == "~") path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        return LocalPaths.Require(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
    }
    public static AppSettings Load(string path)
    {
        path = LocalPaths.Require(path);
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        if ((attributes & FileAttributes.Directory) != 0) throw new IOException("Settings path is not a file.");
        if (new FileInfo(path).Length > 65536) throw new InvalidOperationException(T("SettingsTooLarge"));
        using var document = JsonDocument.Parse(LocalStorage.ReadText(path, 65536));
        var settings = document.RootElement.Deserialize<AppSettings>() ?? throw new InvalidOperationException(T("SettingsEmpty"));
        settings.Language = L10n.NormalizeSetting(settings.Language); settings.RefreshSeconds = Math.Clamp(settings.RefreshSeconds, 15, 3600);
        if (document.RootElement.EnumerateObject().Any(p => p.Name.Equals("OnlineQuota", StringComparison.OrdinalIgnoreCase)))
        {
            try { AtomicJson.Write(path, settings); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { settings.MigrationSaveFailed = true; }
        }
        return settings;
    }
}

public sealed record SettingsLoadResult(AppSettings Settings, bool CanScan, string? WarningKey)
{
    public static SettingsLoadResult Read(string path)
    {
        try { var settings = AppSettings.Load(path); return new(settings, true, settings.MigrationSaveFailed ? "MigrationNotSaved" : null); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        { return new(new AppSettings(), false, "ConfirmFolderAfterError"); }
    }
}

public static class AtomicJson
{
    private static readonly object WriteGate = new();
    public static void Write<T>(string path, T value) => WriteText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    public static void WriteStream(string path, Action<Stream> write, long maximumBytes)
    {
        lock (WriteGate)
        {
            path = LocalPaths.Require(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(LocalPaths.Require(temp), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var bounded = new SizeLimitedWriteStream(file, maximumBytes)) write(bounded);
                LocalPaths.Require(path); File.Move(temp, path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    public static void WriteText(string path, string text)
    {
        lock (WriteGate)
        {
            path = LocalPaths.Require(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(LocalPaths.Require(temp), text, new UTF8Encoding(false)); LocalPaths.Require(path); File.Move(temp, path, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}

public static class CsvExport
{
    public static string Escape(string value)
    {
        string trimmed = value.TrimStart();
        if (trimmed.Length > 0 && ("=+-@".Contains(trimmed[0]) || char.IsControl(value[0]))) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    public static string Build(IEnumerable<UsageEvent> events, PriceCatalog catalog, TimeZoneInfo zone)
    {
        var b = new StringBuilder("date,model,input_tokens,cached_input_tokens,output_tokens,total_tokens,estimated_usd,unpriced_events,qualified_events,pricing_notes\r\n");
        foreach (var day in events.GroupBy(e => UsageSummary.Day(e.At, zone)).OrderBy(g => g.Key))
            foreach (var row in UsageSummary.Group(day, catalog))
                b.AppendLine(string.Join(',', day.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Escape(row.Model), row.Input.ToString(CultureInfo.InvariantCulture), row.Cached.ToString(CultureInfo.InvariantCulture), row.Output.ToString(CultureInfo.InvariantCulture), row.Total.ToString(CultureInfo.InvariantCulture), row.KnownCost.ToString("0.000000", CultureInfo.InvariantCulture), row.Unpriced.ToString(CultureInfo.InvariantCulture), row.Qualified.ToString(CultureInfo.InvariantCulture), Escape(row.PricingNotes)));
        return b.ToString();
    }
}

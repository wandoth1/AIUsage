using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AIUsage.Core;

public sealed class AppSettings
{
    public string CodexHome { get; set; } = "";
    public int RefreshSeconds { get; set; } = 60;
    public bool OnlineQuota { get; set; }
    public bool LightTheme { get; set; }
    public string ResolveHome()
    {
        string path = string.IsNullOrWhiteSpace(CodexHome) ? Environment.GetEnvironmentVariable("CODEX_HOME") ?? "" : CodexHome;
        if (string.IsNullOrWhiteSpace(path)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        path = path.Trim().Trim('"');
        if (path == "~") path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
    }
    public static AppSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 65536) throw new InvalidOperationException("El fichero de ajustes supera 64 KB.");
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? throw new InvalidOperationException("Ajustes vacíos.");
        settings.RefreshSeconds = Math.Clamp(settings.RefreshSeconds, 15, 3600);
        return settings;
    }
}

public static class AtomicJson
{
    public static void Write<T>(string path, T value)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
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
                b.AppendLine(string.Join(',', day.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Escape(row.Model),
                    row.Input.ToString(CultureInfo.InvariantCulture), row.Cached.ToString(CultureInfo.InvariantCulture),
                    row.Output.ToString(CultureInfo.InvariantCulture), row.Total.ToString(CultureInfo.InvariantCulture),
                    row.KnownCost.ToString("0.000000", CultureInfo.InvariantCulture), row.Unpriced.ToString(CultureInfo.InvariantCulture),
                    row.Qualified.ToString(CultureInfo.InvariantCulture), Escape(row.PricingNotes)));
        return b.ToString();
    }
}

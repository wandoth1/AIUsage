namespace AIUsage.Core;

/// Metadata-only description of a candidate Codex data folder: rollout file names and modification
/// times are enumerated with the scanner's own discovery rules; file contents are never opened.
public sealed record CodexFolderCheck(string Path, int Rollouts, int RecentRollouts, bool IsInstallFolder)
{
    public bool HasLogs => Rollouts > 0;
}

/// Detects the local Codex data folder and recognises common misconfigurations, such as selecting
/// the folder where the Codex application is installed instead of the folder holding its logs.
public static class CodexFolder
{
    /// Same window as the scanner, so "recent" matches what the dashboard can show.
    public const int RecentDays = 32;

    /// Folder used when the setting is empty: CODEX_HOME, otherwise %USERPROFILE%\.codex.
    /// Null when that folder is not a permitted local path.
    public static string? AutoDetectedPath()
    {
        try { return new AppSettings().ResolveHome(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    /// Program locations (WindowsApps, Program Files) contain the Codex executable, never its logs.
    public static bool IsInstallFolder(string path)
    {
        string full;
        try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar; }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        if (full.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            string root = Environment.GetFolderPath(folder);
            if (root.Length > 0 && full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// True when a stored CodexHome setting points at an application install folder.
    public static bool IsInstallFolderSetting(string? codexHome)
    {
        if (string.IsNullOrWhiteSpace(codexHome)) return false;
        try { return IsInstallFolder(Environment.ExpandEnvironmentVariables(codexHome.Trim().Trim('"'))); }
        catch (ArgumentException) { return false; }
    }

    public static CodexFolderCheck Inspect(string resolvedPath, DateTimeOffset now, CancellationToken ct = default)
    {
        if (IsInstallFolder(resolvedPath)) return new(resolvedPath, 0, 0, true);
        int warnings = 0;
        var files = LogScanner.Discover(resolvedPath, ref warnings, ct);
        var since = now.AddDays(-RecentDays).UtcDateTime;
        int recent = 0;
        foreach (var file in files)
        {
            try { if (File.GetLastWriteTimeUtc(file) >= since) recent++; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return new(resolvedPath, files.Count, recent, false);
    }

    /// The automatically detected folder, only when it differs from <paramref name="current"/> and holds logs.
    public static CodexFolderCheck? Suggest(string? current, DateTimeOffset now, CancellationToken ct = default)
    {
        string? auto = AutoDetectedPath();
        if (auto is null || (current is not null && SamePath(auto, current))) return null;
        var check = Inspect(auto, now, ct);
        return check.HasLogs ? check : null;
    }

    /// Clears a CodexHome that points at an install folder so automatic detection applies. Returns the removed value.
    public static string? CorrectInstallFolderSetting(AppSettings settings)
    {
        if (!IsInstallFolderSetting(settings.CodexHome)) return null;
        string removed = settings.CodexHome;
        settings.CodexHome = "";
        return removed;
    }

    public static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}

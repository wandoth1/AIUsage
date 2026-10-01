using AIUsage.Core;

// Codex data-folder detection. Synthetic folders only: CODEX_HOME is always redirected to a temporary
// directory, so automatic detection can never reach the real %USERPROFILE%\.codex.
internal static partial class Program
{
    private static void FolderCases()
    {
        Test("Folder: Codex install locations are recognised, data folders are not", () =>
        {
            Require(CodexFolder.IsInstallFolder(@"C:\Program Files\WindowsApps\OpenAI.Codex_26.928.3736.0_x64__2p2nqsd0c76g0\app"), "WindowsApps not recognised");
            Require(CodexFolder.IsInstallFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Codex")), "Program Files not recognised");
            Require(!CodexFolder.IsInstallFolder(Path.Combine(Path.GetTempPath(), ".codex")), "Data folder treated as install folder");
            Require(!CodexFolder.IsInstallFolder(@"C:\Users\someone\.codex"), "Profile folder treated as install folder");
        });
        Test("Folder: an install-folder setting is cleared so automatic detection applies", () =>
        {
            var s = new AppSettings { CodexHome = @"C:\Program Files\WindowsApps\OpenAI.Codex_1.0.0.0_x64__x\app", RefreshSeconds = 90 };
            Equal(@"C:\Program Files\WindowsApps\OpenAI.Codex_1.0.0.0_x64__x\app", CodexFolder.CorrectInstallFolderSetting(s));
            Equal("", s.CodexHome); Equal(90, s.RefreshSeconds);
            var kept = new AppSettings { CodexHome = @"D:\logs\codex" };
            Equal(null, CodexFolder.CorrectInstallFolderSetting(kept)); Equal(@"D:\logs\codex", kept.CodexHome);
            Require(CodexFolder.IsInstallFolderSetting(@"%ProgramFiles%\OpenAI\Codex"), "Expanded install folder not recognised");
            Require(!CodexFolder.IsInstallFolderSetting(""), "Empty setting treated as install folder");
        });
        Test("Folder: inspection counts the rollouts a scan would read, by metadata only", () =>
        {
            using var t = new FolderHome();
            t.File(@"sessions\2026\10\01\rollout-a.jsonl", "{not json: contents are never parsed by detection");
            t.File(@"sessions\2026\08\01\rollout-b.jsonl", "x", DateTime.UtcNow.AddDays(-40));
            t.File(@"archived_sessions\rollout-c.jsonl", "x");
            t.File("history.jsonl", "x"); t.File("auth.json", "SYNTHETIC");
            var c = CodexFolder.Inspect(t.Root, DateTimeOffset.UtcNow);
            Equal(3, c.Rollouts); Equal(2, c.RecentRollouts); Require(c.HasLogs && !c.IsInstallFolder, "Unexpected classification");
        });
        Test("Folder: a folder without sessions has no logs", () =>
        {
            using var t = new FolderHome(); t.File("notes.txt", "x");
            var c = CodexFolder.Inspect(t.Root, DateTimeOffset.UtcNow); Equal(0, c.Rollouts); Require(!c.HasLogs, "Empty folder reported logs");
        });
        Test("Folder: install folders are classified without enumerating them", () =>
        {
            var c = CodexFolder.Inspect(@"C:\Program Files\WindowsApps\Missing.App_1.0_x64__x\app", DateTimeOffset.UtcNow);
            Require(c.IsInstallFolder && !c.HasLogs, "Install folder not classified");
        });
        Test("Folder: suggestion offers the detected folder only when it differs and has logs", () =>
        {
            using var data = new FolderHome(); data.File(@"sessions\2026\10\01\rollout-a.jsonl", "x");
            using var empty = new FolderHome();
            WithCodexHome(data.Root, () =>
            {
                var s = CodexFolder.Suggest(empty.Root, DateTimeOffset.UtcNow);
                Require(s is not null && CodexFolder.SamePath(s.Path, data.Root), "Detected folder not suggested"); Equal(1, s!.RecentRollouts);
                Equal(null, CodexFolder.Suggest(data.Root, DateTimeOffset.UtcNow));
            });
            WithCodexHome(empty.Root, () => Equal(null, CodexFolder.Suggest(data.Root, DateTimeOffset.UtcNow)));
        });
        Test("Folder: automatic detection resolves to CODEX_HOME when set", () =>
        {
            using var data = new FolderHome();
            WithCodexHome(data.Root, () => Require(CodexFolder.SamePath(CodexFolder.AutoDetectedPath()!, data.Root), "CODEX_HOME ignored"));
        });
    }
    private static void WithCodexHome(string path, Action action)
    {
        string? old = Environment.GetEnvironmentVariable("CODEX_HOME");
        try { Environment.SetEnvironmentVariable("CODEX_HOME", path); action(); }
        finally { Environment.SetEnvironmentVariable("CODEX_HOME", old); }
    }
    private sealed class FolderHome : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "AIUsage-folder-" + Guid.NewGuid().ToString("N"));
        public FolderHome() => Directory.CreateDirectory(Root);
        public void File(string relative, string text, DateTime? modifiedUtc = null)
        {
            string path = Path.Combine(Root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, text);
            if (modifiedUtc is { } m) System.IO.File.SetLastWriteTimeUtc(path, m);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}

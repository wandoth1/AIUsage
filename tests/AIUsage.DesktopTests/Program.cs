using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Automation;
using AIUsage.Core;

internal static class Program
{
    private static int passed, failed;
    private static string exe = "", hook = "", control = "", fixture = "", profile = "";
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 3 || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
        { Console.Error.WriteLine("Run only on a clean ephemeral GitHub Actions Windows runner. Arguments: executable, hook DLL, control DLL."); return 2; }
        exe = Path.GetFullPath(args[0]); hook = Path.GetFullPath(args[1]); control = Path.GetFullPath(args[2]);
        profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIUsage");
        if (Directory.Exists(profile)) { Console.Error.WriteLine("Refusing to touch an existing AIUsage profile."); return 2; }
        fixture = Path.Combine(Path.GetTempPath(), "AIUsage-desktop-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
        try
        {
            Test("F04 startup hook positive control executes the harmless marker", () =>
            {
                string marker = Path.Combine(fixture, "positive-hook.txt");
                using var p = Start("dotnet", [control, "--hook-control"], marker);
                Assert(p.WaitForExit(20000) && p.ExitCode == 0 && File.Exists(marker), "Startup-hook positive control failed");
            });
            Test("F01 normal launch warns and never activates a legacy instance", () =>
            {
                using var legacy = new Mutex(false, InstancePolicy.LegacyName(false));
                using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AIUsage.Windows.Show");
                using var process = Start(exe, ["--language", "en"]);
                try
                {
                    WaitUntil(() => Text(process).Contains("An earlier version"), 20000, "No legacy-version warning");
                    Button(process, "OK");
                    Assert(process.WaitForExit(10000) && process.ExitCode == 2, "Legacy conflict not explicit");
                    Assert(!signal.WaitOne(0), "Old show event was signalled");
                    Assert(!Directory.Exists(profile), "Conflict altered the AIUsage profile");
                }
                finally { End(process); }
            });
            Test("F02 readonly migration uses the selected folder in the actual entry point", () => WithProfile(() =>
            {
                string home = MakeHome("readonly", 1); Settings(home, true);
                string path = Path.Combine(profile, "settings.json"), original = File.ReadAllText(path);
                File.SetAttributes(path, FileAttributes.ReadOnly);
                using var process = Start(exe, []);
                try
                {
                    WaitUntil(() => Text(process).Contains("1.1K tokens") && Text(process).Contains("migration could not be saved"), 20000, "Readonly settings discarded");
                    Assert(Text(process).Contains(home), "Active folder not visible");
                    Equal(original, File.ReadAllText(path)); Exit(process);
                }
                finally { End(process); File.SetAttributes(path, FileAttributes.Normal); }
            }));
            Test("F02 corrupted settings pause timers until an explicit folder is saved", () => WithProfile(() =>
            {
                string home = MakeHome("confirmed", 1); File.WriteAllText(Path.Combine(profile, "settings.json"), "{broken}");
                using var process = Start(exe, ["--language", "en"]);
                try
                {
                    WaitUntil(() => Text(process).Contains("Scanning is paused"), 20000, "Missing paused state");
                    // Leave Settings so that the ordinary settings-view pause cannot hide a broken safety guard.
                    Button(process, "Back to usage");
                    Button(process, "Refresh");
                    Thread.Sleep(65000);
                    Assert(!Directory.Exists(Path.Combine(profile, "cache")), "Corrupt settings triggered a scan");
                    Assert(Text(process).Contains("No log folder has been read"), "A default source was selected");
                    Button(process, "Settings");
                    var input = Window(process)!.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "CodexFolderInput"));
                    Assert(input is not null, "Missing folder input"); ((ValuePattern)input!.GetCurrentPattern(ValuePattern.Pattern)).SetValue(home);
                    Button(process, "Save and refresh");
                    WaitUntil(() => Text(process).Contains("1.1K tokens") && Text(process).Contains(home), 20000, "Explicit folder confirmation failed");
                    Exit(process);
                }
                finally { End(process); }
            }));
            Test("F04/F07 published normal mode handles timer refresh and same-version activation with hooks disabled", () => WithProfile(() =>
            {
                string home = MakeHome("timer", 1); Settings(home, true);
                string marker = Path.Combine(fixture, "forbidden-hook.txt"), extraction = Path.Combine(fixture, "bundle-extraction");
                string auth = Path.Combine(home, "auth.json"), config = Path.Combine(home, "config.toml"), history = Path.Combine(home, "history.jsonl");
                var originals = new[] { auth, config, history }.ToDictionary(p => p, Hash);
                using var a = File.Open(auth, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                using var c = File.Open(config, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                using var h = File.Open(history, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                using var process = Start(exe, [], marker, extraction);
                try
                {
                    WaitUntil(() => Text(process).Contains("1.1K tokens"), 20000, "Normal startup failed");
                    Assert(!File.Exists(marker), "Startup hook was executed");
                    Assert(!Directory.Exists(extraction), "Native libraries were self-extracted");
                    Assert(!File.ReadAllText(Path.Combine(profile, "settings.json")).Contains("OnlineQuota"), "Legacy toggle not removed");
                    using (var second = Start(exe, [])) Assert(second.WaitForExit(10000) && second.ExitCode == 0, "Same-version activation failed");
                    string log = Path.Combine(home, "sessions", "rollout-desktop.jsonl");
                    File.AppendAllText(log, Usage(2));
                    WaitUntil(() => Text(process).Contains("2.2K tokens"), 25000, "First automatic refresh failed");
                    File.AppendAllText(log, Usage(3));
                    WaitUntil(() => Text(process).Contains("3.3K tokens"), 25000, "Second automatic refresh failed");
                    string hash = Hash(log); Exit(process); Equal(hash, Hash(log));
                }
                finally { End(process); a.Dispose(); c.Dispose(); h.Dispose(); }
                foreach (var pair in originals) Equal(pair.Value, Hash(pair.Key));
            }));
        }
        finally
        {
            if (Directory.Exists(profile)) Directory.Delete(profile, true);
            Directory.Delete(fixture, true);
        }
        Console.WriteLine($"RESULT: {passed} passed; {failed} failed.");
        Console.WriteLine("Scope: exact published x64 entry point, mutex/tray/timers, UI Automation, synthetic locked files and hooks. Not a network capture or an absence-of-open-attempt trace. No real profile was used.");
        return failed == 0 ? 0 : 1;
    }
    private static Process Start(string file, string[] args, string? marker = null, string? extraction = null)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        start.Environment["CODEX_HOME"] = MakeHome("unselected-default", 5);
        start.Environment.Remove("DOTNET_STARTUP_HOOKS");
        if (marker is not null) { start.Environment["DOTNET_STARTUP_HOOKS"] = hook; start.Environment["AIUSAGE_TEST_HOOK_MARKER"] = marker; }
        if (extraction is not null) start.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = extraction;
        return Process.Start(start) ?? throw new Exception("Test process did not start");
    }
    private static string MakeHome(string name, int count)
    {
        string dir = Path.Combine(fixture, name), sessions = Path.Combine(dir, "sessions");
        if (Directory.Exists(sessions)) return dir;
        Directory.CreateDirectory(sessions);
        string at = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");
        File.WriteAllText(Path.Combine(sessions, "rollout-desktop.jsonl"),
            JsonSerializer.Serialize(new { timestamp = at, type = "session_meta", payload = new { id = "synthetic-desktop-session" } }) + "\n" +
            JsonSerializer.Serialize(new { timestamp = at, type = "turn_context", payload = new { model = "gpt-5.6-sol" } }) + "\n" + Usage(count));
        File.WriteAllText(Path.Combine(dir, "auth.json"), "synthetic locked credentials");
        File.WriteAllText(Path.Combine(dir, "config.toml"), "synthetic locked configuration");
        File.WriteAllText(Path.Combine(dir, "history.jsonl"), "synthetic private history, never needed");
        return dir;
    }
    private static string Usage(int count) => JsonSerializer.Serialize(new
    {
        timestamp = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"), type = "event_msg",
        payload = new { type = "token_count", info = new { total_token_usage = new { input_tokens = count * 1000, output_tokens = count * 100, total_tokens = count * 1100 } } }
    }) + "\n";
    private static void Settings(string home, bool legacy) => File.WriteAllText(Path.Combine(profile, "settings.json"), JsonSerializer.Serialize(new { CodexHome = home, Language = "en", RefreshSeconds = 15, OnlineQuota = legacy }));
    private static void WithProfile(Action action)
    {
        Assert(!Directory.Exists(profile), "Refusing an existing profile"); Directory.CreateDirectory(profile);
        try { action(); } finally { if (Directory.Exists(profile)) Directory.Delete(profile, true); }
    }
    private static AutomationElement? Window(Process process)
    {
        if (process.HasExited) return null;
        return AutomationElement.RootElement.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id));
    }
    private static string Text(Process process)
    {
        try
        {
            var window = Window(process); if (window is null) return "";
            return string.Join("\n", window.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>().Select(e => e.Current.Name));
        }
        catch (ElementNotAvailableException) { return ""; }
    }
    private static void Button(Process process, string name)
    {
        WaitUntil(() =>
        {
            try
            {
                var b = Window(process)?.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button), new PropertyCondition(AutomationElement.NameProperty, name)));
                if (b is null) return false; ((InvokePattern)b.GetCurrentPattern(InvokePattern.Pattern)).Invoke(); return true;
            }
            catch (ElementNotAvailableException) { return false; }
        }, 10000, "Missing button: " + name);
    }
    private static void WaitUntil(Func<bool> condition, int timeout, string message)
    {
        var clock = Stopwatch.StartNew(); do { if (condition()) return; Thread.Sleep(250); } while (clock.ElapsedMilliseconds < timeout);
        throw new Exception(message);
    }
    private static void Exit(Process process) { Button(process, "Exit"); Assert(process.WaitForExit(10000) && process.ExitCode == 0, "Application exit failed"); }
    private static void End(Process process) { if (!process.HasExited) { process.Kill(true); process.WaitForExit(10000); } }
    private static string Hash(string path) { using var f = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(f)); }
    private static void Test(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e); } }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual) => Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; got {actual}");
}

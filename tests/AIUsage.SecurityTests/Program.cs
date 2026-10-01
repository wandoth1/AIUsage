using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AIUsage.Core;

internal static class Program
{
    private static int passed, failed;
    private static readonly DateTimeOffset At = DateTimeOffset.UtcNow.AddMinutes(-1);
    private static int Main(string[] args)
    {
        if (args.Contains("--hook-control")) return 0;
        L10n.SetLanguage("en");
        Test("F01 local-only and legacy instance identities are distinct", () =>
        {
            foreach (bool demo in new[] { false, true }) Assert(InstancePolicy.InstanceName(demo) != InstancePolicy.LegacyName(demo), "Legacy identity reused");
            Assert(InstancePolicy.InstanceName(false) != InstancePolicy.InstanceName(true), "Demo shares production identity");
            Assert(InstancePolicy.EventName(false) != InstancePolicy.EventName(true), "Demo shares production event");
        });
        Test("F01 detecting a legacy mutex never signals its show event", () =>
        {
            if (!OperatingSystem.IsWindows()) return;
            using var legacy = new Mutex(false, InstancePolicy.LegacyName(false));
            using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AIUsage.Windows.Show");
            Assert(InstancePolicy.LegacyRunning(false), "Legacy instance not detected");
            Assert(!signal.WaitOne(0), "Legacy process was activated");
        });
        Test("F02 readonly migration preserves the selected folder", () => Temp(root =>
        {
            var path = Path.Combine(root, "settings.json");
            string json = JsonSerializer.Serialize(new { CodexHome = root, OnlineQuota = true, Language = "es", RefreshSeconds = 120 });
            File.WriteAllText(path, json); File.SetAttributes(path, FileAttributes.ReadOnly);
            try
            {
                var loaded = SettingsLoadResult.Read(path);
                Assert(loaded.CanScan, "Valid settings were discarded"); Equal(root, loaded.Settings.CodexHome);
                Equal("es", loaded.Settings.Language); Equal(120, loaded.Settings.RefreshSeconds);
                if (OperatingSystem.IsWindows()) { Equal("MigrationNotSaved", loaded.WarningKey); Equal(json, File.ReadAllText(path)); }
            }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
        }));
        Test("F02 corrupted settings require confirmation rather than defaults", () => Temp(root =>
        {
            var p = Path.Combine(root, "settings.json");
            foreach (string json in new[] { "{broken}", "[]", "null", "{\"Language\":{}}", "{\"RefreshSeconds\":\"bad\"}" })
            {
                File.WriteAllText(p, json); var load = SettingsLoadResult.Read(p);
                Assert(!load.CanScan, "Corrupt configuration permits scanning"); Equal("ConfirmFolderAfterError", load.WarningKey);
            }
        }));
        Test("F02 oversized/deep settings do not select a default source", () => Temp(root =>
        {
            string p = Path.Combine(root, "settings.json");
            File.WriteAllText(p, new string(' ', 65537)); Assert(!SettingsLoadResult.Read(p).CanScan, "Oversized settings accepted");
            File.WriteAllText(p, "{\"x\":" + new string('[', 100) + "0" + new string(']', 100) + "}");
            Assert(!SettingsLoadResult.Read(p).CanScan, "Deep settings accepted");
        }));
        Test("F02 first use and successful migration retain supported defaults", () => Temp(root =>
        {
            string p = Path.Combine(root, "settings.json"); Assert(SettingsLoadResult.Read(p).CanScan, "First use disabled");
            File.WriteAllText(p, "{\"OnlineQuota\":true,\"Language\":\"es\"}"); var load = SettingsLoadResult.Read(p);
            Assert(load.CanScan && load.WarningKey is null, "Valid migration failed");
            Assert(!File.ReadAllText(p).Contains("OnlineQuota"), "Legacy toggle remains");
        }));
        Test("F06 a root history file is never opened or cached", () => Temp(root =>
        {
            string p = Path.Combine(root, "history.jsonl"); File.WriteAllText(p, "private synthetic history");
            using var blocked = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var scan = new LogScanner(Path.Combine(root, "cache")).Scan(root);
            Equal(0, scan.Files); Equal(0, scan.Warnings); Equal(0, scan.Events.Count);
        }));
        Test("F06 direct rollout folders require rollout- filenames", () => Temp(root =>
        {
            WriteLog(Path.Combine(root, "notes.jsonl"), 1); WriteLog(Path.Combine(root, "rollout-synthetic.jsonl"), 2);
            var scan = new LogScanner(Path.Combine(root, "cache")).Scan(root);
            Equal(1, scan.Files); Equal(220L, scan.Events.Sum(e => e.Tokens.Total));
        }));
        Test("F06 history is excluded even inside a sessions directory", () => Temp(root =>
        {
            var dir = Path.Combine(root, "sessions"); Directory.CreateDirectory(dir);
            WriteLog(Path.Combine(dir, "history.jsonl"), 9); WriteLog(Path.Combine(dir, "synthetic.jsonl"), 1);
            var scan = new LogScanner(Path.Combine(root, "cache")).Scan(root);
            Equal(1, scan.Files); Equal(110L, scan.Events.Sum(e => e.Tokens.Total));
        }));
        Test("F05 small compact checkpoints reload exactly", () => Temp(root =>
        {
            string p = Path.Combine(root, "checkpoint.json"); var c = Sample(3); CacheStore.Save(p, c);
            var loaded = CacheStore.Load(p)!; Assert(c.Events.SequenceEqual(loaded.Events), "Small cache changed events");
            Assert(!File.ReadAllText(p).Contains("\n  "), "Indented cache retained"); Equal(1, Directory.GetFiles(root, "*.json").Length);
        }));
        Test("F05 paged checkpoints preserve every event and share byte limits", () => Temp(root =>
        {
            string p = Path.Combine(root, "checkpoint.json"); var c = Sample(CacheStore.PageEvents * 2 + 1); CacheStore.Save(p, c);
            var loaded = CacheStore.Load(p)!; Assert(c.Events.SequenceEqual(loaded.Events), "Paged events changed");
            Equal(3, loaded.EventPages!.Count);
            Assert(Directory.GetFiles(root, "*.json").All(f => new FileInfo(f).Length <= CacheStore.MaxFileBytes), "Unreadable file written");
        }));
        Test("F05 missing or corrupt pages invalidate the entire checkpoint", () => Temp(root =>
        {
            string p = Path.Combine(root, "checkpoint.json"); var c = Sample(CacheStore.PageEvents + 1); CacheStore.Save(p, c);
            File.WriteAllText(Path.Combine(root, c.EventPages![0]), "{broken}"); Assert(CacheStore.Load(p) is null, "Partial totals accepted");
            CacheStore.Save(p, c); File.Delete(Path.Combine(root, c.EventPages![0])); Assert(CacheStore.Load(p) is null, "Missing page accepted");
        }));
        Test("F05 page path traversal and count mismatches are rejected", () => Temp(root =>
        {
            string p = Path.Combine(root, "checkpoint.json"); var c = Sample(0); c.EventPages = ["../outside.json"]; c.StoredEventCount = 10;
            AtomicJson.Write(p, c); Assert(CacheStore.Load(p) is null, "Unsafe page path accepted");
            c.EventPages = null; c.StoredEventCount = 1; AtomicJson.Write(p, c); Assert(CacheStore.Load(p) is null, "Wrong event count accepted");
        }));
        Test("F05 interrupted oversized writes preserve a valid prior checkpoint", () => Temp(root =>
        {
            string p = Path.Combine(root, "checkpoint.json"); CacheStore.Save(p, Sample(1));
            var oversized = Sample(CacheStore.PageEvents + 1); oversized.Prefix = new string('x', checked((int)CacheStore.MaxFileBytes + 1));
            Throws<IOException>(() => CacheStore.Save(p, oversized));
            Equal(1, CacheStore.Load(p)!.Events.Count); Equal(0, Directory.GetFiles(root, "*.tmp").Length); Equal(1, Directory.GetFiles(root, "*.json").Length);
        }));
        Test("F05 replacement removes obsolete pages only after commit", () => Temp(root =>
        {
            string p = Path.Combine(root, "checkpoint.json"); var c = Sample(CacheStore.PageEvents + 1); CacheStore.Save(p, c);
            string[] old = c.EventPages!.ToArray(); c.Events.RemoveRange(1, c.Events.Count - 1); CacheStore.Save(p, c);
            Equal(1, CacheStore.Load(p)!.Events.Count); Assert(old.All(n => !File.Exists(Path.Combine(root, n))), "Old pages leaked");
        }));
        Test("F05 scanner restarts and appends after a paged checkpoint", () => Temp(root =>
        {
            string p = Path.Combine(root, "sessions", "rollout-test.jsonl"); int count = CacheStore.PageEvents + 1;
            WriteLog(p, count); string cache = Path.Combine(root, "cache"); new LogScanner(cache).Scan(root);
            var resumed = new LogScanner(cache); var scan = resumed.Scan(root);
            Equal(1, resumed.PersistentCacheHits); Equal((long)count * 110, scan.Events.Sum(e => e.Tokens.Total));
            File.AppendAllText(p, UsageLine(count + 1)); scan = resumed.Scan(root); Equal((long)(count + 1) * 110, scan.Events.Sum(e => e.Tokens.Total));
            resumed.ClearCache(); Equal(0, Directory.GetFiles(cache, "*.json").Length);
        }));
        Test("F02 safety messages have both language variants", () =>
        {
            foreach (string code in new[] { "en", "es" }) foreach (string key in new[] { "MigrationNotSaved", "ConfirmFolderAfterError", "EarlierVersionRunning", "ActiveFolder", "ChooseExplicitFolder" })
                Assert(L10n.ResourceStrings(code).ContainsKey(key), "Missing safety translation");
        });
        if (args.Contains("--stress")) Test("F05 500000-event persistence, dashboard and reload", () => Temp(root => Stress(root, 500_000)));
        Console.WriteLine($"RESULT: {passed} passed; {failed} failed.");
        return failed == 0 ? 0 : 1;
    }
    private static void Stress(string root, int count)
    {
        string path = Path.Combine(root, "sessions", "rollout-stress.jsonl"), cache = Path.Combine(root, "cache");
        WriteLog(path, count);
        long before = GC.GetTotalMemory(true);
        var watch = Stopwatch.StartNew(); var reader = new LogScanner(cache); var first = reader.Scan(root); watch.Stop();
        long initialMs = watch.ElapsedMilliseconds, heap = GC.GetTotalMemory(true) - before;
        Equal(count, first.Events.Count); Equal((long)count * 110, first.Events.Sum(e => e.Tokens.Total));
        var summaries = DashboardData.Create(first.Events, PriceCatalog.Load(), TimeZoneInfo.Utc, UsageSummary.Day(At, TimeZoneInfo.Utc));
        Equal((long)count * 110, summaries.ForPeriod(1).Single().Total); Equal(count * 0.0006m, summaries.ForPeriod(1).Single().KnownCost);
        long cacheBytes = Directory.GetFiles(cache, "*.json").Sum(p => new FileInfo(p).Length);
        long max = Directory.GetFiles(cache, "*.json").Max(p => new FileInfo(p).Length);
        Assert(max <= CacheStore.MaxFileBytes, "A cache file exceeds its reader limit");
        var reload = new LogScanner(cache); watch.Restart(); var second = reload.Scan(root); watch.Stop();
        Equal(1, reload.PersistentCacheHits); Equal(count, second.Events.Count); Equal((long)count * 110, second.Events.Sum(e => e.Tokens.Total));
        Console.WriteLine($"STRESS source_bytes={new FileInfo(path).Length}; events={count}; initial_ms={initialMs}; reload_ms={watch.ElapsedMilliseconds}; retained_heap_delta_bytes={heap}; cache_bytes={cacheBytes}; largest_cache_file_bytes={max}; persistent_cache_hits={reload.PersistentCacheHits}");
        GC.KeepAlive(reader); GC.KeepAlive(first);
    }
    private static FileCache Sample(int count) => new()
    {
        Schema = LogScanner.ParserSchemaVersion, Prefix = "synthetic",
        Events = Enumerable.Range(1, count).Select(i => new UsageEvent(At, "synthetic", new Tokens(100, 0, 10, 0, 110), "standard", "session", new Tokens(i * 100L, 0, i * 10L, 0, i * 110L), i)).ToList()
    };
    private static void WriteLog(string path, int count)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine(JsonSerializer.Serialize(new { timestamp = At.ToString("O"), type = "session_meta", payload = new { id = "synthetic-security-session" } }));
        writer.WriteLine(JsonSerializer.Serialize(new { timestamp = At.ToString("O"), type = "turn_context", payload = new { model = "gpt-5.6-sol" } }));
        for (int i = 1; i <= count; i++) writer.Write(UsageLine(i));
    }
    private static string UsageLine(int i) => "{\"timestamp\":\"" + At.ToString("O") + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":" + i * 100L + ",\"output_tokens\":" + i * 10L + ",\"total_tokens\":" + i * 110L + "}}}}\n";
    private static void Temp(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "AIUsage-security-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try { action(root); } finally { Directory.Delete(root, true); }
    }
    private static void Test(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e); } }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual) => Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; got {actual}");
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}

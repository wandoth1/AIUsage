using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using AIUsage.Core;

// All credentials, messages, paths and sessions below are synthetic and local to a temporary directory.
internal static class Program
{
    private static int passed, failed, skipped;
    private static string repositoryRoot = "";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow.AddMinutes(-1);
    private static int Main(string[] args)
    {
        repositoryRoot = Option(args, "--repo-root") ?? throw new ArgumentException("--repo-root is required");
        L10n.SetLanguage("en");
        using var network = new NetworkEvents();
        Test("Authenticated client type is absent from the application assembly", () => Require(typeof(AppSettings).Assembly.GetType("AIUsage.Core.CodexUsageClient") is null, "Online client remains"));
        Test("No setting property can enable an online mode", () => Require(typeof(AppSettings).GetProperty("OnlineQuota") is null, "Online property remains"));
        Test("Core compiled metadata has no networking, credential or process-launch APIs", () => CheckAssembly(typeof(AppSettings).Assembly.Location));
        Test("Legacy true setting is removed and harmless preferences survive", () => Temp(root =>
        {
            string path = Path.Combine(root, "settings.json");
            File.WriteAllText(path, "{\"OnlineQuota\":true,\"Language\":\"es\",\"RefreshSeconds\":120,\"LightTheme\":true,\"CodexHome\":\"unchanged\"}");
            var s = AppSettings.Load(path);
            Equal("es", s.Language); Equal(120, s.RefreshSeconds); Equal("unchanged", s.CodexHome); Require(s.LightTheme, "Theme lost");
            Require(!File.ReadAllText(path).Contains("OnlineQuota"), "Legacy toggle remained on disk");
        }));
        Test("Legacy switch variants cannot restore account access", () => Temp(root =>
        {
            string path = Path.Combine(root, "settings.json");
            foreach (string value in new[] { "true", "false", "null", "\"true\"", "{}" })
            {
                File.WriteAllText(path, "{\"OnlineQuota\":" + value + ",\"Language\":\"en\"}"); Equal("en", AppSettings.Load(path).Language);
                Require(!File.ReadAllText(path).Contains("OnlineQuota"), "Obsolete field survived");
            }
        }));
        Test("Default settings never serialize the removed online field", () => Require(!JsonSerializer.Serialize(new AppSettings()).Contains("OnlineQuota"), "Obsolete setting serialized"));
        Test("Repeated settings migration is idempotent", () => Temp(root =>
        {
            string p = Path.Combine(root, "settings.json"); File.WriteAllText(p, "{\"OnlineQuota\":true,\"Language\":\"es\"}");
            AppSettings.Load(p); byte[] before = File.ReadAllBytes(p); AppSettings.Load(p); Require(before.SequenceEqual(File.ReadAllBytes(p)), "Repeated migration changed settings");
        }));
        Test("Scanning succeeds with exclusively locked synthetic authentication and configuration", () => Temp(root =>
        {
            Fixture(root); string auth = Path.Combine(root, "auth.json"), config = Path.Combine(root, "config.toml");
            File.WriteAllText(auth, "DO-NOT-READ-SYNTHETIC-CREDENTIAL"); File.WriteAllText(config, "DO-NOT-MODIFY-SYNTHETIC-CONFIG");
            byte[] a = File.ReadAllBytes(auth), c = File.ReadAllBytes(config);
            using (var al = new FileStream(auth, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (var cl = new FileStream(config, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var scanner = new LogScanner(Path.Combine(root, "cache")); var one = scanner.Scan(root); var two = scanner.Scan(root);
                Equal(0, one.Warnings); Equal(1100L, one.Events.Sum(e => e.Tokens.Total)); Equal(1100L, two.Events.Sum(e => e.Tokens.Total));
            }
            Require(a.SequenceEqual(File.ReadAllBytes(auth)) && c.SequenceEqual(File.ReadAllBytes(config)), "Source credentials/config changed");
        }));
        Test("Source rollout bytes and modification time never change", () => Temp(root =>
        {
            string p = Fixture(root); byte[] before = File.ReadAllBytes(p); var at = File.GetLastWriteTimeUtc(p);
            var scanner = new LogScanner(Path.Combine(root, "cache")); scanner.Scan(root); scanner.ClearCache(); scanner.Scan(root);
            Require(before.SequenceEqual(File.ReadAllBytes(p)), "Original log changed"); Equal(at, File.GetLastWriteTimeUtc(p));
        }));
        Test("Malformed credential files are irrelevant to accounting", () => Temp(root =>
        {
            Fixture(root); File.WriteAllText(Path.Combine(root, "auth.json"), "{broken}");
            Equal(1100L, new LogScanner(Path.Combine(root, "cache")).Scan(root).Events.Sum(e => e.Tokens.Total));
        }));
        Test("No model request is needed for tokens, prices, dashboard or CSV", () => Temp(root =>
        {
            Fixture(root); var result = new LogScanner(Path.Combine(root, "cache")).Scan(root);
            var prices = PriceCatalog.Load(); var rows = UsageSummary.Group(result.Events, prices);
            Equal(1100L, rows.Single().Total); Equal(0.006m, rows.Single().KnownCost);
            var dashboard = DashboardData.Create(result.Events, prices, TimeZoneInfo.Utc, UsageSummary.Day(Now, TimeZoneInfo.Utc));
            Equal(1100L, dashboard.ForPeriod(1).Single().Total); Require(CsvExport.Build(result.Events, prices, TimeZoneInfo.Utc).Contains("0.006000"), "CSV mismatch");
        }));
        Test("Local quota snapshots retain their original observation time", () => Temp(root =>
        {
            Fixture(root); var scan = new LogScanner(Path.Combine(root, "cache")).Scan(root);
            var row = QuotaSelection.Select(scan.Quotas).Single(); Equal(Now, row.Snapshot.At); Equal(12d, row.Window.UsedPercent); Equal("Local log", row.Snapshot.Source);
        }));
        Test("No recorded limits stays empty rather than making a request", () => Equal(0, QuotaSelection.Select(Array.Empty<QuotaSnapshot>()).Count));
        Test("UNC, WSL, URL and device paths are rejected before file access", () =>
        {
            foreach (string path in new[] { @"\\example.invalid\share\logs", @"\\wsl.localhost\Ubuntu\home\user\.codex", "//example.invalid/share", @"\\?\C:\logs", @"\\.\pipe\name", "https://example.invalid/logs", "file://example.invalid/share" }) Throws<LocalPathException>(() => LocalPaths.Require(path));
        });
        Test("Mapped network and unknown drives are not accepted as local", () =>
        {
            foreach (var kind in new[] { DriveType.Network, DriveType.Unknown, DriveType.NoRootDirectory }) Require(!LocalPaths.IsLocalDrive(kind), "Non-local drive permitted");
            Require(LocalPaths.IsLocalDrive(DriveType.Fixed) && LocalPaths.IsLocalDrive(DriveType.Removable), "Local drive rejected");
        });
        Test("Relative paths cannot invoke an unintended working-directory lookup", () =>
        { foreach (string path in new[] { "relative", "", "   ", @"C:relative" }) Throws<LocalPathException>(() => LocalPaths.Require(path)); });
        Test("Remote CODEX_HOME is rejected and the environment is restored", () =>
        {
            string? old = Environment.GetEnvironmentVariable("CODEX_HOME");
            try { Environment.SetEnvironmentVariable("CODEX_HOME", @"\\example.invalid\share"); Throws<LocalPathException>(() => new AppSettings().ResolveHome()); }
            finally { Environment.SetEnvironmentVariable("CODEX_HOME", old); }
        });
        Test("Resident local paths with spaces and Unicode are accepted", () => Temp(root =>
        {
            string path = Path.Combine(root, "datos españoles", "new.json"); Equal(Path.GetFullPath(path), LocalPaths.Require(path));
            AtomicJson.Write(path, new { ok = true }); Require(File.Exists(path), "Local write failed");
        }));
        Test("Links, offline files and remote-recall attributes are blocked", () =>
        {
            foreach (FileAttributes a in new[] { FileAttributes.ReparsePoint, FileAttributes.Offline, (FileAttributes)0x40000, (FileAttributes)0x400000 }) Require(!LocalPaths.IsResident(FileAttributes.Archive | a), "Remote/link attribute accepted");
            Require(LocalPaths.IsResident(FileAttributes.Archive), "Regular local file rejected");
        });
        Test("Directory link ancestors are rejected before opening their contents", () => Temp(root =>
        {
            string target = Path.Combine(root, "real"), link = Path.Combine(root, "link"); Directory.CreateDirectory(target);
            try { Directory.CreateSymbolicLink(link, target); }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException || (e is IOException && (e.HResult & 0xffff) == 1314)) { throw new Skip("Host cannot create a synthetic symbolic link: " + e.GetType().Name); }
            Throws<LocalPathException>(() => LocalPaths.Require(Path.Combine(link, "data.jsonl")));
        }));
        Test("Remote settings, cache, price and export destinations are rejected", () => Temp(root =>
        {
            string remote = @"\\example.invalid\share";
            Throws<LocalPathException>(() => AppSettings.Load(remote + @"\settings.json"));
            Throws<LocalPathException>(() => AtomicJson.Write(remote + @"\settings.json", new AppSettings()));
            Throws<LocalPathException>(() => PriceCatalog.Load(remote + @"\prices.json"));
            Throws<LocalPathException>(() => LocalStorage.ExportCsv(remote, "test")); Throws<LocalPathException>(() => new LogScanner(remote).Scan(root));
        }));
        Test("CSV export creates unique local files without overwriting source files", () => Temp(root =>
        {
            string one = LocalStorage.ExportCsv(root, "date,model\r\n2026-10-01,test"), two = LocalStorage.ExportCsv(root, "different");
            Require(one != two && Path.GetDirectoryName(one) == Path.Combine(root, "exports"), "Export escaped its directory");
            Require(File.ReadAllText(one).StartsWith("date,model"), "CSV changed"); Equal("different", File.ReadAllText(two));
        }));
        Test("Built-in price validation rejects bad JSON before saving", () => Temp(root =>
        {
            string p = Path.Combine(root, "price-overrides.json"); File.WriteAllText(p, "{}");
            Throws<JsonException>(() => LocalStorage.SavePrices(p, "{\"a\":{\"Input\":1}}")); Equal("{}", File.ReadAllText(p));
        }));
        Test("Built-in price editor saves only valid local overrides", () => Temp(root =>
        {
            string p = Path.Combine(root, "price-overrides.json"), json = "{\"custom\":{\"Input\":2,\"Cached\":1,\"Output\":3}}";
            LocalStorage.SavePrices(p, json); Equal(json, LocalStorage.ReadText(p, 256 * 1024)); Require(PriceCatalog.Load(p).HasOverrides, "Custom rates absent");
        }));
        Test("Bounded local editor reads reject oversized files", () => Temp(root =>
        {
            string p = Path.Combine(root, "oversized.json"); File.WriteAllText(p, new string('x', 100));
            Throws<IOException>(() => LocalStorage.ReadText(p, 99)); Equal(100, LocalStorage.ReadText(p, 100).Length);
        }));
        Test("Both languages expose the local boundary, not an online option", () =>
        {
            foreach (string code in new[] { "en", "es" })
            {
                var values = L10n.ResourceStrings(code); Require(values.ContainsKey("LocalOnlyHelp"), "Local-only help missing");
                Require(!values.ContainsKey("OnlineOption") && !values.ContainsKey("OnlineConsent"), "Online UI remains");
            }
        });
        Test("Private conversation text never enters the local metadata cache", () => Temp(root =>
        {
            string p = Fixture(root); File.AppendAllText(p, Row("response_item", new { text = "PRIVATE-SYNTHETIC-TEXT" }));
            string cache = Path.Combine(root, "cache"); new LogScanner(cache).Scan(root);
            Require(Directory.GetFiles(cache, "*.json").All(f => !File.ReadAllText(f).Contains("PRIVATE-SYNTHETIC-TEXT")), "Conversation persisted");
        }));
        Test("Local-source guards reject network APIs, credential readers and external launchers", () =>
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.cs", SearchOption.AllDirectories).Where(p => !p.Split(Path.DirectorySeparatorChar).Any(x => x is "bin" or "obj")))
            {
                string text = File.ReadAllText(file);
                foreach (string forbidden in new[] { "System.Net", "Process.Start", "ProcessStartInfo", "ShellExecute", "WebBrowser", "FolderBrowserDialog", "SaveFileDialog", "OpenFileDialog", "backend-api", "access_token", "refresh_token", "auth.json", "account/rateLimits/read" }) Require(!text.Contains(forbidden, StringComparison.Ordinal), "Forbidden runtime source: " + Path.GetFileName(file) + " / " + forbidden);
            }
        });
        Test("URI loaders and dynamic activation are excluded from reviewed API allowlists", () =>
        {
            foreach (string assembly in new[] { "AIUsage", "AIUsage.Core" })
            {
                var policy = File.ReadAllLines(Path.Combine(repositoryRoot, "tests", "AIUsage.OfflineTests", "allowed-api", assembly + ".txt"));
                foreach (string type in new[] { "System.Windows.Media.Imaging.BitmapImage", "System.Windows.Media.Imaging.BitmapDecoder", "System.Xml.XmlReader", "System.Xml.Linq.XDocument", "System.Windows.Markup.XamlReader", "System.Windows.Controls.Frame", "System.Windows.Navigation.NavigationWindow", "System.Activator", "System.Runtime.Loader.AssemblyLoadContext", "System.Runtime.InteropServices.NativeLibrary", "System.Net.Http.HttpClient" }) Require(!policy.Contains("T " + type), "Dangerous type allowlisted: " + type);
            }
        });
        var assemblies = args.Select((value, index) => (value, index)).Where(x => x.value == "--app-assembly").Select(x => args[x.index + 1]).ToArray();
        if (assemblies.Length == 0) { Console.WriteLine("FAIL No --app-assembly supplied; Windows metadata guard was not executed"); failed++; }
        foreach (string assembly in assemblies) Test("Windows application compiled API boundary: " + Path.GetFileName(Path.GetDirectoryName(assembly)), () => CheckAssembly(assembly));
        Test("No HTTP, socket or DNS start event observed during synthetic local operations", () => Require(network.Events.Count == 0, "Network start events: " + string.Join(",", network.Events)));
        Console.WriteLine($"RESULT: {passed} passed; {failed} failed; {skipped} skipped.");
        Console.WriteLine("Scope: application-owned assembly/source guards and synthetic operations; not a packet capture or a guarantee about Windows/security/cloud-sync services.");
        return failed == 0 ? 0 : 1;
    }
    private static string? Option(string[] args, string key) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    private static void CheckAssembly(string path)
    {
        using var stream = File.OpenRead(path); using var pe = new PEReader(stream); var m = pe.GetMetadataReader();
        string assemblyName = m.GetString(m.GetAssemblyDefinition().Name);
        string policyPath = Path.Combine(repositoryRoot, "tests", "AIUsage.OfflineTests", "allowed-api", assemblyName + ".txt");
        var allowed = File.ReadAllLines(policyPath).Where(l => !l.StartsWith('#')).ToHashSet(StringComparer.Ordinal);
        var unreviewed = m.AssemblyReferences.Select(h => "A " + m.GetString(m.GetAssemblyReference(h).Name)).Where(n => !allowed.Contains(n)).ToList();
        string FullName(TypeReferenceHandle h)
        {
            var t = m.GetTypeReference(h);
            return t.ResolutionScope.Kind == HandleKind.TypeReference ? FullName((TypeReferenceHandle)t.ResolutionScope) + "/" + m.GetString(t.Name) : (m.GetString(t.Namespace).Length > 0 ? m.GetString(t.Namespace) + "." : "") + m.GetString(t.Name);
        }
        unreviewed.AddRange(m.TypeReferences.Select(h => "T " + FullName(h)).Where(n => !allowed.Contains(n)));
        Require(unreviewed.Count == 0, "Unreviewed references in " + assemblyName + ": " + string.Join(" | ", unreviewed));
        foreach (var handle in m.TypeReferences)
        {
            var type = m.GetTypeReference(handle); string ns = m.GetString(type.Namespace), name = m.GetString(type.Name);
            Require(!(ns == "System.Net" || ns.StartsWith("System.Net.", StringComparison.Ordinal)), "Network type: " + ns + "." + name);
            Require(!(ns == "System.Diagnostics" && name.StartsWith("Process", StringComparison.Ordinal)), "Process API: " + name);
            Require(!(ns == "Microsoft.Win32" && (name.StartsWith("Registry") || name.EndsWith("FileDialog"))), "Credential/shell API: " + name);
            Require(name is not ("WebBrowser" or "Hyperlink" or "FolderBrowserDialog"), "External navigation: " + name);
        }
        foreach (var handle in m.MethodDefinitions)
        {
            var method = m.GetMethodDefinition(handle);
            if ((method.Attributes & MethodAttributes.PinvokeImpl) == 0) continue;
            var import = method.GetImport(); Equal("user32.dll", m.GetString(m.GetModuleReference(import.Module).Name).ToLowerInvariant()); Equal("DestroyIcon", m.GetString(import.Name));
        }
        foreach (var handle in m.MemberReferences)
        {
            var member = m.GetMemberReference(handle); string name = m.GetString(member.Name);
            if (member.Parent.Kind != HandleKind.TypeReference) continue;
            var type = m.GetTypeReference((TypeReferenceHandle)member.Parent);
            if (m.GetString(type.Namespace) == "System.Reflection" && m.GetString(type.Name) == "Assembly") Require(!name.StartsWith("Load", StringComparison.Ordinal) && name is not ("GetType" or "CreateInstance"), "Dynamic assembly loading/activation is not permitted");
            string full = FullName((TypeReferenceHandle)member.Parent);
            Require(!(full == "System.Type" && name is "GetType" or "InvokeMember"), "Dynamic type lookup is not permitted");
            Require(!(full == "System.Runtime.InteropServices.Marshal" && (name.Contains("DelegateForFunctionPointer") || name.Contains("GetFunctionPointerForDelegate"))), "Dynamic native invocation is not permitted");
        }
    }
    private static string Fixture(string root)
    {
        string directory = Path.Combine(root, "sessions"); Directory.CreateDirectory(directory); string path = Path.Combine(directory, "rollout.jsonl");
        File.WriteAllText(path, Row("session_meta", new { id = "synthetic-local-session" }) + Row("turn_context", new { model = "gpt-5.6-sol" }) +
            Row("event_msg", new { type = "token_count", info = new { total_token_usage = new { input_tokens = 1000, output_tokens = 100, total_tokens = 1100 } }, rate_limits = new { primary = new { used_percent = 12, window_minutes = 300 } } }));
        return path;
    }
    private static string Row(string type, object payload) => JsonSerializer.Serialize(new { timestamp = Now.ToString("O"), type, payload }) + "\n";
    private static void Temp(Action<string> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "AIUsage-local-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try { test(root); } finally { Directory.Delete(root, true); }
    }
    private static void Test(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Skip e) { skipped++; Console.WriteLine("SKIP " + name + ": " + e.Message); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.GetType().Name + ": " + e.Message); }
    }
    private sealed class Skip(string message) : Exception(message);
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual) => Require(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; got {actual}");
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private sealed class NetworkEvents : EventListener
    {
        public ConcurrentQueue<string> Events { get; } = new();
        protected override void OnEventSourceCreated(EventSource source) { if (source.Name.StartsWith("System.Net", StringComparison.Ordinal)) EnableEvents(source, EventLevel.Verbose); }
        protected override void OnEventWritten(EventWrittenEventArgs e) { if (e.EventName is "RequestStart" or "ConnectStart" or "ResolutionStart" or "AcceptStart") Events.Enqueue(e.EventSource.Name + "/" + e.EventName); }
    }
}

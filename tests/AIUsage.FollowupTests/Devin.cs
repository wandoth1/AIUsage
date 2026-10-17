using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIUsage.Core;

// Devin session database. Every database here is synthetic and created in a temporary folder with Windows'
// own SQLite; the scanner always receives an explicit path, so the real %APPDATA%\Devin is never opened.
internal static partial class Program
{
    private static void DevinCases()
    {
        Test("Devin: usage maps to tokens, effort and list prices; copies are counted once", () =>
        {
            using var t = new DevinHome();
            t.Exec(DevinHome.Schema);
            t.Message("user", null, null, 0, 0, 0, 0, text: "SYNTHETIC-DEVIN-PROMPT");
            for (int copy = 0; copy < 3; copy++) t.Message("assistant", "msg-swe", "swe-2-high", 1000, 9000, null, 500);
            t.Message("tool", null, null, 0, 0, 0, 0);
            for (int copy = 0; copy < 2; copy++) t.Message("assistant", "msg-gpt", "gpt-6-astra-high", 100, 10000, 2000, 100);
            t.Message("assistant", "msg-claude", "claude-opus-5-5-high", 0, 0, 0, 1_000_000);
            t.Message("assistant", "msg-compact", "compactor", 5000, null, null, 100);
            var scan = new DevinLogScanner(t.Cache).Scan(t.Database);
            Equal(4, scan.Events.Count); Equal(0, scan.Warnings);
            var swe = scan.Events.Single(e => e.Model == "swe-2");
            Equal(new Tokens(10000, 9000, 500, 0, 10500), swe.Tokens); Equal("high", swe.Effort);
            Equal(0.0132m, ClaudePrices.Cost(swe));                                              // 1K×$3 + 9K×$0.30 + 500×$15
            var gpt = scan.Events.Single(e => e.Model == "gpt-6-astra");
            Equal(new Tokens(12100, 10000, 100, 0, 12200, 2000), gpt.Tokens);
            Equal(0.041m, ClaudePrices.Cost(gpt));                                               // 100×$10 + 10K×$1 + 2K×$12.50 + 100×$50
            Equal(20m, ClaudePrices.Cost(scan.Events.Single(e => e.Model == "claude-opus-5-5"))); // 1M output × $20
            Equal(null, ClaudePrices.Cost(scan.Events.Single(e => e.Model == "compactor")));
        });
        Test("Devin: an idle database is read without creating side files or changing it", () =>
        {
            using var t = new DevinHome();
            t.Exec(DevinHome.Schema); t.Message("assistant", "m1", "swe-2-max", 10, 0, null, 5);
            string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(t.Database)));
            Equal(1, new DevinLogScanner(t.Cache).Scan(t.Database).Events.Count);
            Equal(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(t.Database))));
            Equal("sessions.db", string.Join(",", Directory.GetFiles(Path.GetDirectoryName(t.Database)!).Select(Path.GetFileName)));
        });
        Test("Devin: a database in use (write-ahead log) is read, including uncheckpointed rows", () =>
        {
            using var t = new DevinHome();
            t.Exec("pragma journal_mode=wal; pragma wal_autocheckpoint=0;" + DevinHome.Schema);
            t.Message("assistant", "m1", "swe-2-medium", 100, 0, null, 10);
            Require(new FileInfo(t.Database + "-wal").Length > 0, "The synthetic log is empty");
            var scanner = new DevinLogScanner(t.Cache);
            Equal(110L, scanner.Scan(t.Database).Events.Sum(e => e.Tokens.Total));
            t.Message("assistant", "m2", "swe-2-medium", 200, 0, null, 20);
            Equal(330L, scanner.Scan(t.Database).Events.Sum(e => e.Tokens.Total));
        });
        Test("Devin: new rows are read incrementally; the cache holds no content or raw ids", () =>
        {
            using var t = new DevinHome();
            t.Exec(DevinHome.Schema); t.Message("assistant", "message-id-one", "swe-2-high", 100, 0, null, 10, text: "SYNTHETIC-DEVIN-ANSWER");
            var scanner = new DevinLogScanner(t.Cache); Equal(110L, scanner.Scan(t.Database).Events.Sum(e => e.Tokens.Total));
            t.Message("assistant", "message-id-one", "swe-2-high", 100, 0, null, 10);   // a later copy of the same response
            t.Message("assistant", "message-id-two", "swe-2-high", 200, 0, null, 20);
            Equal(330L, scanner.Scan(t.Database).Events.Sum(e => e.Tokens.Total));
            var fresh = new DevinLogScanner(t.Cache);
            Equal(330L, fresh.Scan(t.Database).Events.Sum(e => e.Tokens.Total)); Equal(1, fresh.PersistentCacheHits);
            string cached = File.ReadAllText(Path.Combine(t.Cache, "devin.json"));
            Require(!cached.Contains("SYNTHETIC-DEVIN") && !cached.Contains("message-id") && !cached.Contains("synthetic-session") && !cached.Contains("req-"), "Cache retains content or raw ids");
        });
        Test("Devin: a replaced database or an incoherent cache starts again", () =>
        {
            using var t = new DevinHome();
            t.Exec(DevinHome.Schema);
            for (int i = 0; i < 5; i++) t.Message("assistant", "old-" + i, "swe-2-high", 100, 0, null, 0);
            var scanner = new DevinLogScanner(t.Cache); Equal(500L, scanner.Scan(t.Database).Events.Sum(e => e.Tokens.Total));
            File.Delete(t.Database); t.Exec(DevinHome.Schema); t.Message("assistant", "new-1", "swe-2-high", 7, 0, null, 0);
            Equal(7L, scanner.Scan(t.Database).Events.Sum(e => e.Tokens.Total));
            string cache = Path.Combine(t.Cache, "devin.json");
            File.WriteAllText(cache, File.ReadAllText(cache).Replace("\"Input\":7", "\"Input\":-7"));
            var fresh = new DevinLogScanner(t.Cache); Equal(7L, fresh.Scan(t.Database).Events.Sum(e => e.Tokens.Total)); Equal(0, fresh.PersistentCacheHits);
        });
        Test("Devin: missing, foreign or malformed data fails softly", () =>
        {
            using var t = new DevinHome();
            Equal(0, new DevinLogScanner(t.Cache).Scan(t.Database).Events.Count);                 // no database yet
            File.WriteAllText(t.Database, "this is not a SQLite database, just synthetic text");
            bool failed = false;
            try { new DevinLogScanner(t.Cache).Scan(t.Database); } catch (IOException) { failed = true; }
            Require(failed, "A non-database file was accepted");
            File.Delete(t.Database); t.Exec("create table other(x);");
            failed = false;
            try { new DevinLogScanner(t.Cache).Scan(t.Database); } catch (IOException) { failed = true; }
            Require(failed, "A database without message_nodes was accepted");
            File.Delete(t.Database); t.Exec(DevinHome.Schema);
            t.Raw("{\"role\":\"assistant\",\"metadata\":{\"generation_model\":\"swe-2-high\",\"metrics\":{\"input_tokens\":-5,\"output_tokens\":1}}}");
            t.Raw("{\"role\":\"assistant\",\"metadata\":{\"generation_model\":\"swe-2-high\",\"metrics\":{\"input_tokens\":\"many\"}}}");
            t.Raw("{\"role\":\"assistant\",\"generation_model\" broken json");
            t.Message("assistant", "ok", "swe-2-high", 1, 0, null, 1);
            var scan = new DevinLogScanner(t.Cache).Scan(t.Database); Equal(1, scan.Events.Count); Equal(1, scan.Warnings);
        });
        Test("Devin: model ids split into catalog model, effort and tier", () =>
        {
            Equal(("swe-2", "high", "standard"), DevinLogScanner.SplitModel("swe-2-high"));
            Equal(("claude-opus-5-5", "xhigh", "standard"), DevinLogScanner.SplitModel("Claude-Opus-5-5-XHigh"));
            Equal(("gpt-6.1-sol", "medium", "standard"), DevinLogScanner.SplitModel("gpt-6-1-sol-medium"));
            Equal(("gpt-6.1-sol", "high", "priority"), DevinLogScanner.SplitModel("gpt-6-1-sol-high-priority"));
            Equal(("gpt-5.6-luna", "none", "standard"), DevinLogScanner.SplitModel("gpt-5-6-luna-none"));
            Equal(("gpt-6-astra", (string?)null, "standard"), DevinLogScanner.SplitModel("gpt-6-astra"));
            Equal(("claude-opus-5-5", "high", "fast"), DevinLogScanner.SplitModel("claude-opus-5-5-high-fast"));
            Equal(("claude-sonnet-4-6", (string?)null, "standard"), DevinLogScanner.SplitModel("claude-sonnet-4-6-thinking-1m"));
            Equal(("glm-5-2", "max", "standard"), DevinLogScanner.SplitModel("glm-5-2-max-1m"));
            Equal(("swe-1-7-lightning", "medium", "standard"), DevinLogScanner.SplitModel("swe-1-7-lightning-medium"));
            Equal(("swe-1-6-fast", (string?)null, "standard"), DevinLogScanner.SplitModel("swe-1-6-fast"));   // a model variant, not a tier
            Equal(("gemini-3.8-flash", "medium", "standard"), DevinLogScanner.SplitModel("gemini-3-8-flash-medium"));
            Equal(("compactor", (string?)null, "standard"), DevinLogScanner.SplitModel("compactor"));
            Equal(("unknown", (string?)null, "standard"), DevinLogScanner.SplitModel("bad model <script>")); Equal(("unknown", (string?)null, "standard"), DevinLogScanner.SplitModel(new string('a', 81)));
        });
        Test("Devin: every model it offers is priced; priority and fast variants cost double", () =>
        {
            using var t = new DevinHome();
            t.Exec(DevinHome.Schema);
            t.Message("assistant", "sol", "gpt-6-1-sol-high", 1000, 9000, null, 100);
            t.Message("assistant", "sol-p", "gpt-6-1-sol-high-priority", 1000, 9000, null, 100);
            t.Message("assistant", "opus-f", "claude-opus-5-5-high-fast", 0, 0, null, 1000);
            t.Message("assistant", "kimi", "kimi-k3-high", 1000, 0, null, 1000);
            t.Message("assistant", "glm", "glm-5-3-max", 1000, 1000, null, 1000);
            t.Message("assistant", "gemini", "gemini-3-8-flash-medium", 1000, 0, 1000, 1000);
            t.Message("assistant", "light", "swe-1-7-lightning-medium", 1000, 1000, null, 1000);
            t.Message("assistant", "fast16", "swe-1-6-fast", 1000, 0, null, 1000);
            var events = new DevinLogScanner(t.Cache).Scan(t.Database).Events;
            decimal? Cost(string model, string tier = "standard") => ClaudePrices.Cost(events.Single(e => e.Model == model && e.Tier == tier));
            Equal(0.0039m, Cost("gpt-6.1-sol"));               // 1K×$2 + 9K×$0.10 + 100×$10
            Equal(0.0078m, Cost("gpt-6.1-sol", "priority"));   // priority processing ×2
            Equal(0.04m, Cost("claude-opus-5-5", "fast"));     // 1K×$20 ×2
            Equal(0.018m, Cost("kimi-k3"));                    // 1K×$3 + 1K×$15
            Equal(0.00606m, Cost("glm-5-3"));                  // 1K×$1.40 + 1K×$0.26 + 1K×$4.40
            Equal(0.00525m, Cost("gemini-3.8-flash"));         // Google: 1K×$0.75 + 1K×$0.75 (cache write) + 1K×$3.75
            Equal(0.016m, Cost("swe-1-7-lightning"));          // 1K×$2.50 + 1K×$1 + 1K×$12.50
            Equal(0.003m, Cost("swe-1-6-fast"));               // 1K×$0.50 + 1K×$2.50: same price as SWE-1.6
            foreach (string model in new[] { "swe-1-7", "glm-5-2", "glm-5-3-flash", "gemini-3.7-flash", "gemini-4-argon", "gemini-3.1-pro", "kimi-k2-7", "deepseek-v4-pro", "grok-4-7", "adaptive", "swe-check" })
                Require(ClaudePrices.Cost(new UsageEvent(At, model, new Tokens(10, 0, 10, 0, 20))) is not null, "No price for " + model);
        });
        Test("Devin: settings keep the source opt-in and accept a database override", () =>
        {
            Require(!new AppSettings().Devin, "Devin must be off by default");
            using var t = new DevinHome(); string path = Path.Combine(t.Root, "settings.json");
            AtomicJson.Write(path, new AppSettings { Devin = true, DevinDatabase = t.Database });
            var loaded = AppSettings.Load(path); Require(loaded.Devin, "Setting not persisted"); Equal(Path.GetFullPath(t.Database), loaded.ResolveDevinDatabase());
            Require(new AppSettings().ResolveDevinDatabase().EndsWith(@"\Devin\cli\sessions.db", StringComparison.OrdinalIgnoreCase), "Unexpected default location");
        });
    }
    /// A synthetic Devin data folder. Uses winsqlite3 directly to WRITE test data; production code can only read.
    private sealed class DevinHome : IDisposable
    {
        public const string Schema = "create table message_nodes(row_id integer primary key autoincrement, session_id text not null, node_id integer not null, parent_node_id integer, chat_message text not null, created_at integer not null, metadata text);";
        [DllImport("winsqlite3.dll")] private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("winsqlite3.dll")] private static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr argument, IntPtr error);
        [DllImport("winsqlite3.dll")] private static extern int sqlite3_close_v2(IntPtr db);
        private IntPtr db; private int node;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "AIUsage-devin-" + Guid.NewGuid().ToString("N"));
        public string Database => Path.Combine(Root, "cli", "sessions.db");
        public string Cache => Path.Combine(Root, "aiusage-cache");
        public DevinHome() => Directory.CreateDirectory(Path.GetDirectoryName(Database)!);
        public void Exec(string sql)
        {
            if (db == IntPtr.Zero && sqlite3_open_v2(Encoding.UTF8.GetBytes(Database + "\0"), out db, 0x2 | 0x4, IntPtr.Zero) != 0) throw new InvalidOperationException("Cannot create the synthetic database");
            if (sqlite3_exec(db, Encoding.UTF8.GetBytes(sql + "\0"), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != 0) throw new InvalidOperationException("Synthetic SQL failed: " + sql);
            // Without a write-ahead log, close so the file is idle (as when Devin is not running).
            if (!File.Exists(Database + "-wal")) { sqlite3_close_v2(db); db = IntPtr.Zero; }
        }
        public void Message(string role, string? id, string? model, long input, long? read, long? write, long output, string text = "synthetic text")
        {
            var metadata = new Dictionary<string, object?> { ["created_at"] = At.ToString("O"), ["telemetry"] = new { source = role } };
            if (model is not null)
            {
                metadata["generation_model"] = model; metadata["request_id"] = "req-" + id; metadata["num_tokens"] = output;
                metadata["metrics"] = new Dictionary<string, object?> { ["input_tokens"] = input, ["cache_read_tokens"] = read, ["cache_creation_tokens"] = write, ["output_tokens"] = output, ["total_time_ms"] = 1200 };
            }
            Raw(JsonSerializer.Serialize(new Dictionary<string, object?> { ["message_id"] = id ?? Guid.NewGuid().ToString(), ["role"] = role, ["content"] = text, ["metadata"] = metadata }));
        }
        public void Raw(string chatMessage) =>
            Exec($"insert into message_nodes(session_id, node_id, chat_message, created_at) values('synthetic-session', {++node}, '{chatMessage.Replace("'", "''")}', {At.ToUnixTimeSeconds()});");
        public void Dispose()
        {
            if (db != IntPtr.Zero) sqlite3_close_v2(db);
            Directory.Delete(Root, true);
        }
    }
}

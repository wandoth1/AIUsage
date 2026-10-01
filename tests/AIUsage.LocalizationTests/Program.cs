using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIUsage.Core;

// Synthetic, offline localization regressions. No real Codex paths or credentials are read.
internal static class Program
{
    private static int passed, failed;
    private static readonly DateTimeOffset Stamp = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static int Main()
    {
        Test("English and Spanish resource sets have identical keys", () =>
        {
            var en = L10n.ResourceStrings("en"); var es = L10n.ResourceStrings("es");
            Require(en.Count >= 100 && en.Keys.Order().SequenceEqual(es.Keys.Order()), "Missing translation keys");
            Require(en.Values.Concat(es.Values).All(s => !string.IsNullOrWhiteSpace(s)), "Empty translation");
        });
        Test("All localized format placeholders match", () =>
        {
            var en = L10n.ResourceStrings("en"); var es = L10n.ResourceStrings("es");
            foreach (var (key, value) in en)
            {
                string[] Fields(string s) => Regex.Matches(s, @"\{\d+(?:[^{}]*)\}").Select(m => m.Value).Order().ToArray();
                Require(Fields(value).SequenceEqual(Fields(es[key])), "Placeholder mismatch: " + key);
                if (Fields(value).Length == 0) continue;
                foreach (var language in new[] { "en", "es" }) { L10n.SetLanguage(language); System.Text.CompositeFormat.Parse(L10n.T(key)); }
            }
        });
        Test("Spanish regional system cultures select Spanish", () =>
        { foreach (var name in new[] { "es-ES", "es-MX", "es-AR" }) Equal("es", L10n.ResolveLanguage("auto", CultureInfo.GetCultureInfo(name))); });
        Test("Other system cultures fall back to English", () =>
        { foreach (var name in new[] { "en-GB", "fr-FR", "ja-JP", "" }) Equal("en", L10n.ResolveLanguage("auto", CultureInfo.GetCultureInfo(name))); });
        Test("Explicit language overrides the system language", () =>
        {
            Equal("en", L10n.ResolveLanguage("en", CultureInfo.GetCultureInfo("es-ES"))); Equal("es", L10n.ResolveLanguage("es", CultureInfo.GetCultureInfo("en-US")));
        });
        Test("Unknown and null preferences safely normalize", () =>
        { Equal("auto", L10n.NormalizeSetting(null)); Equal("auto", L10n.NormalizeSetting("../../fr")); Equal("es", L10n.NormalizeSetting(" ES ")); });
        Test("Old settings migrate without discarding existing options", () => WithTemp(path =>
        {
            File.WriteAllText(path, "{\"RefreshSeconds\":120,\"OnlineQuota\":true,\"LightTheme\":true,\"CodexHome\":\"synthetic\"}");
            var s = AppSettings.Load(path); Equal("auto", s.Language); Equal(120, s.RefreshSeconds); Require(s.LightTheme && s.CodexHome == "synthetic", "Settings lost"); Require(!File.ReadAllText(path).Contains("OnlineQuota"), "Obsolete online option not removed");
        }));
        Test("Language persists across settings reload", () => WithTemp(path =>
        {
            foreach (var language in new[] { "en", "es", "auto" }) { AtomicJson.Write(path, new AppSettings { Language = language, RefreshSeconds = 45 }); var s = AppSettings.Load(path); Equal(language, s.Language); Equal(45, s.RefreshSeconds); }
        }));
        Test("Malformed language value cannot crash culture selection", () => WithTemp(path =>
        {
            File.WriteAllText(path, "{\"Language\":null}"); Equal("auto", AppSettings.Load(path).Language); File.WriteAllText(path, "{\"Language\":\"unknown-culture\"}"); Equal("auto", AppSettings.Load(path).Language);
        }));
        Test("English and Spanish UI strings switch without restart", () =>
        {
            L10n.SetLanguage("en"); Equal("Settings", L10n.T("Settings")); Equal("Save and refresh", L10n.T("SaveRefresh"));
            L10n.SetLanguage("es"); Equal("Ajustes", L10n.T("Settings")); Equal("Guardar y actualizar", L10n.T("SaveRefresh")); L10n.SetLanguage("en"); Equal("Settings", L10n.T("Settings"));
        });
        Test("Presentation uses chosen locale and always labels dollars", () =>
        {
            L10n.SetLanguage("en"); Equal("$1,234.50", UsageSummary.Dollars(1234.5m, L10n.Culture)); Equal("1.5M", UsageSummary.Compact(1500000, L10n.Culture));
            L10n.SetLanguage("es"); Equal("$1.234,50", UsageSummary.Dollars(1234.5m, L10n.Culture)); Equal("1,5M", UsageSummary.Compact(1500000, L10n.Culture));
        });
        Test("Changing UI language does not mutate global parsing culture", () =>
        {
            var current = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture; L10n.SetLanguage("es"); Equal(current.Name, CultureInfo.CurrentCulture.Name); Equal(ui.Name, CultureInfo.CurrentUICulture.Name);
        });
        Test("Localized placeholders format all arguments", () =>
        {
            L10n.SetLanguage("en"); Equal("Resets in 2 d 5 h", L10n.F("ResetDays", 2, 5)); Equal("34.5% used", L10n.F("PercentUsed", 34.5));
            L10n.SetLanguage("es"); Equal("Reinicio en 2 d 5 h", L10n.F("ResetDays", 2, 5)); Equal("34,5% usado", L10n.F("PercentUsed", 34.5));
        });
        Test("Quota serialization and identities do not change with language", () =>
        {
            using var doc = JsonDocument.Parse("{\"primary\":{\"used_percent\":15,\"window_minutes\":300},\"secondary\":{\"used_percent\":25,\"window_minutes\":10080},\"credits\":{\"unlimited\":true}}");
            L10n.SetLanguage("en"); var en = QuotaParser.Parse(doc.RootElement, Stamp, "Local log"); L10n.SetLanguage("es"); var es = QuotaParser.Parse(doc.RootElement, Stamp, "Local log");
            Equal(JsonSerializer.Serialize(en), JsonSerializer.Serialize(es)); Equal("Codex · Session", es.Windows[0].Name);
            Equal("Codex · Sesión", L10n.WindowName(es.Windows[0])); Equal("Codex · Semanal", L10n.WindowName(es.Windows[1])); L10n.SetLanguage("en"); Equal("Codex · Session", L10n.WindowName(es.Windows[0]));
        });
        Test("Legacy cached labels translate without changing provider identity", () =>
        {
            L10n.SetLanguage("en"); var window = new LimitWindow("Custom provider · Semanal", 32, null, 604800, "provider:604800");
            Equal("Custom provider · Weekly", L10n.WindowName(window)); Equal("provider:604800", window.Id); Equal("Local log", L10n.SourceName("Registro local"));
        });
        Test("External names and values remain untouched", () =>
        {
            L10n.SetLanguage("es"); Equal("Provider X", L10n.SourceName("Provider X")); Equal("12.34", L10n.CreditValue("12.34"));
            Equal("Sin límite", L10n.CreditValue("Unlimited")); Equal("UnknownLabel", L10n.WindowName(new("UnknownLabel", 1, null, null)));
        });
        Test("Costs and token totals are invariant across languages", () =>
        {
            var e = new UsageEvent(Stamp, "gpt-5.6-sol", new(200000, 0, 100000, 0, 300000)); var catalog = PriceCatalog.Load();
            L10n.SetLanguage("en"); var en = UsageSummary.Group([e], catalog).Single(); L10n.SetLanguage("es"); var es = UsageSummary.Group([e], catalog).Single();
            Equal(2.8m, en.KnownCost); Equal(en.KnownCost, es.KnownCost); Equal(300000L, es.Total); Equal(en.Qualified, es.Qualified);
            Require(en.PricingNotes.StartsWith("API promotion") && es.PricingNotes.StartsWith("Promoción API"), "Promotion not localized");
        });
        Test("CSV schema and numerical columns stay invariant", () =>
        {
            var e = new UsageEvent(Stamp, "gpt-reserve", new(1000, 0, 100, 0, 1100)); var c = PriceCatalog.Load();
            L10n.SetLanguage("en"); string en = CsvExport.Build([e], c, TimeZoneInfo.Utc); L10n.SetLanguage("es"); string es = CsvExport.Build([e], c, TimeZoneInfo.Utc);
            Equal(en.Split('\n')[0], es.Split('\n')[0]); Equal(en.Split('\n')[1].Split(',')[6], es.Split('\n')[1].Split(',')[6]);
            Require(en.Contains("Inherited OpenUsage alias") && es.Contains("Equivalencia heredada"), "CSV notes not localized");
        });
        Test("User custom notes are never translated or discarded", () => WithTemp(path =>
        {
            File.WriteAllText(path, "{\"custom\":{\"Input\":2,\"Cached\":1,\"Output\":10,\"Note\":\"My private pricing note\"}}");
            var c = PriceCatalog.Load(path); var e = new UsageEvent(Stamp, "custom", new(1000, 0, 100, 0, 1100));
            foreach (var code in new[] { "en", "es" }) { L10n.SetLanguage(code); var quote = c.Quote(e); Equal<decimal?>(0.003m, quote.Cost); Require(quote.Note.Contains("My private pricing note"), "User note changed"); }
        }));
        Test("Pricing errors and local-only notices are localized", () =>
        {
            foreach (var code in new[] { "en", "es" })
            {
                L10n.SetLanguage(code); var q = PriceCatalog.Load().Quote(new(Stamp, "no-known-price", new(1000, 0, 100, 0, 1100)));
                Equal(L10n.T("UnverifiedPrice"), q.Note); Equal<decimal?>(null, q.Cost);
                Require(L10n.T("LocalOnlyHelp").Contains("auth.json") && L10n.T("LocalPathBlocked").Contains("WSL"), "Required local-only notice missing");
            }
        });
        Test("Independent application version is used in About", () =>
        {
            Equal("1.4.1", AppVersion.Value);
            foreach (var code in new[] { "en", "es" }) { L10n.SetLanguage(code); var about = L10n.F("About", AppVersion.Value); Require(about.Contains("AIUsage 1.4.1") && about.Contains("OpenUsage") && !about.Contains("v0.7.12"), "About conflates versions"); }
        });
        Test("Concurrent localized reads return complete strings", () =>
        {
            Parallel.For(0, 1000, i => { L10n.SetLanguage(i % 2 == 0 ? "en" : "es"); string text = L10n.F("ResetDays", 2, 5); Require(text is "Resets in 2 d 5 h" or "Reinicio en 2 d 5 h", "Mixed locale template"); });
        });
        L10n.SetLanguage("en"); Console.WriteLine($"RESULT: {passed} passed; {failed} failed."); return failed == 0 ? 0 : 1;
    }
    private static void WithTemp(Action<string> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "AIUsage-i18n-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try { test(Path.Combine(root, "synthetic.json")); } finally { Directory.Delete(root, true); }
    }
    private static void Test(string name, Action test) { try { test(); passed++; Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; got {actual}"); }
}

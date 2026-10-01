using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;

[assembly: NeutralResourcesLanguage("en")]

namespace AIUsage.Core;

/// Presentation-only localization. Serialized identifiers and accounting never depend on language.
public static class L10n
{
    private sealed record Locale(string Code, CultureInfo Culture);
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;
    private static readonly ResourceManager Resources = new("AIUsage.Core.Resources.Strings", typeof(L10n).Assembly);
    private static Locale current = Create(ResolveLanguage("auto", SystemCulture));
    public static string Code => Volatile.Read(ref current).Code;
    public static CultureInfo Culture => Volatile.Read(ref current).Culture;
    public static string NormalizeSetting(string? setting) => setting?.Trim().ToLowerInvariant() switch
    { "en" => "en", "es" => "es", _ => "auto" };
    public static string ResolveLanguage(string? setting, CultureInfo systemCulture) => NormalizeSetting(setting) switch
    { "en" => "en", "es" => "es", _ => systemCulture.TwoLetterISOLanguageName == "es" ? "es" : "en" };
    private static Locale Create(string code) => new(code, CultureInfo.GetCultureInfo(code == "es" ? "es-ES" : "en-US"));
    public static void SetLanguage(string? setting) => Volatile.Write(ref current, Create(ResolveLanguage(setting, SystemCulture)));
    public static string T(string key) => Get(key, Volatile.Read(ref current).Culture);
    public static string F(string key, params object?[] args)
    {
        var locale = Volatile.Read(ref current);
        return string.Format(locale.Culture, Get(key, locale.Culture), args);
    }
    private static string Get(string key, CultureInfo culture) => Resources.GetString(key, culture)
        ?? throw new InvalidOperationException("Missing localization key: " + key);

    // Used by regression tests to verify the actual embedded resources, including the satellite.
    public static IReadOnlyDictionary<string, string> ResourceStrings(string language)
    {
        var culture = language == "es" ? CultureInfo.GetCultureInfo("es") : CultureInfo.InvariantCulture;
        var set = Resources.GetResourceSet(culture, true, false) ?? throw new InvalidOperationException("Missing resource set: " + language);
        return set.Cast<DictionaryEntry>().ToDictionary(x => (string)x.Key, x => (string)x.Value!, StringComparer.Ordinal);
    }
    public static string WindowName(LimitWindow window)
    {
        int separator = window.Name.LastIndexOf(" · ", StringComparison.Ordinal);
        if (separator < 0) return window.Name;
        string suffix = window.Name[(separator + 3)..];
        string translated = suffix switch
        {
            "Session" or "Sesión" => T("Session"),
            "Weekly" or "Semanal" => T("Weekly"),
            _ => window.Seconds is > 0 ? (window.Seconds.Value / 3600d).ToString("0.#", Culture) + " h" : suffix
        };
        return window.Name[..(separator + 3)] + translated;
    }
    public static string SourceName(string source) => source switch
    {
        "Local log" or "Registro local" => T("LocalLog"),
        "Online account" or "Cuenta · consulta online" => T("OnlineAccount"),
        _ => source // External provider names and user content are never translated.
    };
    public static string CreditValue(string value) => value is "Unlimited" or "Sin límite" ? T("Unlimited") : value;
    public static string BundledPriceNote(string note) => note == Get("SolPromoNote", CultureInfo.InvariantCulture) ? T("SolPromoNote") : note;
}

public static class AppVersion
{
    public static string Value { get; } = typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion.Split('+')[0] ?? typeof(AppVersion).Assembly.GetName().Version!.ToString(3);
}

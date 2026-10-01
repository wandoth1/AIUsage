using static AIUsage.Core.L10n;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AIUsage.Core;

public sealed record ModelRate(decimal Input, decimal Cached, decimal Output,
    decimal CacheWriteMultiplier = 1, long LongThreshold = 0, decimal FastMultiplier = 2, string Source = "",
    string Note = "", bool TierRulesVerified = true, bool LongCacheVerified = true, DateOnly? ReviewAfter = null);
public sealed record PriceQuote(decimal? Cost, string PricingModel, string Note = "");

public sealed class PriceCatalog
{
    private readonly Dictionary<string, ModelRate> rates;
    private readonly HashSet<string> customKeys = new(StringComparer.OrdinalIgnoreCase);
    public const string SnapshotDate = "2026-10-01";
    public bool HasOverrides => customKeys.Count > 0;
    public PriceCatalog(Dictionary<string, ModelRate> rates) => this.rates = Normalize(rates);
    public static PriceCatalog Load(string? overridesPath = null)
    {
        using var stream = typeof(PriceCatalog).Assembly.GetManifestResourceStream("AIUsage.Core.pricing.json") ?? throw new InvalidOperationException(T("MissingCatalog"));
        var catalog = new PriceCatalog(ReadEntries(stream));
        if (overridesPath is not null && File.Exists(overridesPath))
        {
            using var customFile = new FileStream(overridesPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // A bounded snapshot also protects against a file growing after the initial length check.
            using var buffer = new MemoryStream();
            byte[] bytes = new byte[256 * 1024 + 1];
            int count = customFile.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            if (count > 256 * 1024) throw new InvalidOperationException(T("PricesTooLarge"));
            buffer.Write(bytes, 0, count); buffer.Position = 0;
            foreach (var (key, rate) in ReadEntries(buffer))
            {
                catalog.rates[key] = rate;
                catalog.customKeys.Add(key);
            }
        }
        return catalog;
    }
    private static Dictionary<string, ModelRate> ReadEntries(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException(T("ExpectedPricesObject"));
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        var entries = new Dictionary<string, ModelRate>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in doc.RootElement.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object) throw new JsonException(T("ExpectedRateObject"));
            var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in entry.Value.EnumerateObject())
                if (!fields.Add(p.Name)) throw new JsonException(T("DuplicatePriceFields"));
            var rate = entry.Value.Deserialize<ModelRate>(options) ?? throw new JsonException(T("NullPrice"));
            string key = entry.Name.Trim();
            Validate(key, rate);
            if (!entries.TryAdd(key, rate)) throw new JsonException(T("DuplicateNormalizedModels"));
        }
        return entries;
    }
    private static Dictionary<string, ModelRate> Normalize(Dictionary<string, ModelRate> entries)
    {
        var result = new Dictionary<string, ModelRate>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, rate) in entries)
        {
            Validate(key, rate);
            if (!result.TryAdd(key.Trim(), rate)) throw new InvalidOperationException(T("DuplicateModels"));
        }
        return result;
    }
    private static void Validate(string key, ModelRate r)
    {
        if (r is null || string.IsNullOrWhiteSpace(key) || key.Length > 200 || key.Any(char.IsControl) ||
            r.Input is < 0 or > 1_000_000 || r.Cached is < 0 or > 1_000_000 || r.Output is < 0 or > 1_000_000 ||
            r.FastMultiplier is <= 0 or > 100 || r.CacheWriteMultiplier is <= 0 or > 100 || r.LongThreshold < 0 ||
            r.Source is null || r.Note is null || r.Source.Length > 2000 || r.Note.Length > 2000)
            throw new InvalidOperationException(T("InvalidPrice"));
    }
    public decimal? Cost(UsageEvent e) => Quote(e).Cost;
    public PriceQuote Quote(UsageEvent e)
    {
        string model = e.Model.Trim().ToLowerInvariant();
        if (model.StartsWith("openai/", StringComparison.Ordinal)) model = model[7..];
        model = Regex.Replace(model, @"-\d{4}-?\d{2}-?\d{2}$", "", RegexOptions.CultureInvariant);
        bool fastAlias = model.EndsWith("-fast", StringComparison.Ordinal);
        if (fastAlias) model = model[..^5];
        model = Regex.Replace(model, @"-\d{4}-?\d{2}-?\d{2}$", "", RegexOptions.CultureInvariant);
        var notes = new List<string>();
        // Explicit user tariffs take precedence over an inferred upstream alias.
        if (!rates.ContainsKey(model) && (model == "gpt-reserve" ||
            (model == "codex-auto-review" && e.At >= new DateTimeOffset(2026, 7, 9, 0, 0, 0, TimeSpan.Zero))))
        {
            notes.Add(F("AliasNote", model));
            model = "gpt-5.6-luna";
        }
        if (!rates.TryGetValue(model, out var rate)) return new(null, model, T("UnverifiedPrice"));
        string serviceTier = e.Tier.Trim().ToLowerInvariant();
        decimal tier = serviceTier switch
        {
            "standard" or "default" or "auto" or "" => 1,
            "fast" or "priority" => rate.FastMultiplier,
            "flex" or "batch" => 0.5m,
            _ => -1
        };
        if (tier < 0) return new(null, model, T("UnknownTier"));
        if (fastAlias) tier = rate.FastMultiplier; // Apply once, not twice.
        bool large = rate.LongThreshold > 0 && e.Tokens.Input > rate.LongThreshold;
        if (customKeys.Contains(model)) notes.Add(T("CustomRateNote"));
        if (rate.Note.Length > 0) notes.Add(customKeys.Contains(model) ? rate.Note : L10n.BundledPriceNote(rate.Note));
        if (!rate.TierRulesVerified && tier != 1) notes.Add(T("TierNote"));
        if (!rate.LongCacheVerified && large && e.Tokens.Cached > 0) notes.Add(T("LongCacheNote"));
        if (rate.ReviewAfter is { } review && DateOnly.FromDateTime(DateTime.UtcNow) >= review)
            notes.Add(T("PromoReviewNote"));
        decimal inputFactor = large ? 2 : 1, outputFactor = large ? 1.5m : 1;
        var t = e.Tokens;
        long cached = Math.Clamp(t.Cached, 0, t.Input);
        long write = Math.Clamp(t.CacheWrite, 0, t.Input - cached);
        decimal cost = ((t.Input - cached - write) * rate.Input * inputFactor + cached * rate.Cached * inputFactor +
            write * rate.Input * rate.CacheWriteMultiplier * inputFactor + t.Output * rate.Output * outputFactor) * tier / 1_000_000m;
        return new(cost, model, string.Join(" ", notes));
    }
}

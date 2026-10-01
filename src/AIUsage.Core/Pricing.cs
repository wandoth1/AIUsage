using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIUsage.Core;

public sealed record ModelRate(decimal Input, decimal Cached, decimal Output,
    decimal CacheWriteMultiplier = 1, long LongThreshold = 0, decimal FastMultiplier = 2, string Source = "");

public sealed class PriceCatalog
{
    private readonly Dictionary<string, ModelRate> rates;
    public const string SnapshotDate = "2026-10-01";
    public bool HasOverrides { get; private set; }
    public PriceCatalog(Dictionary<string, ModelRate> rates) => this.rates = rates;
    public static PriceCatalog Load(string? overridesPath = null)
    {
        using var stream = typeof(PriceCatalog).Assembly.GetManifestResourceStream("AIUsage.Core.pricing.json") ?? throw new InvalidOperationException("Falta el catálogo de precios.");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var entries = JsonSerializer.Deserialize<Dictionary<string, ModelRate>>(stream, options) ?? new();
        var catalog = new PriceCatalog(entries);
        if (overridesPath is not null && File.Exists(overridesPath))
        {
            if (new FileInfo(overridesPath).Length > 256 * 1024) throw new InvalidOperationException("El fichero de precios supera 256 KB.");
            var custom = JsonSerializer.Deserialize<Dictionary<string, ModelRate>>(File.ReadAllText(overridesPath), options) ?? throw new InvalidOperationException("Precios personalizados no válidos.");
            foreach (var (key, r) in custom)
            {
                if (r is null || string.IsNullOrWhiteSpace(key) || r.Input < 0 || r.Cached < 0 || r.Output < 0 ||
                    r.Input > 1_000_000 || r.Cached > 1_000_000 || r.Output > 1_000_000 || r.FastMultiplier is < 0 or > 100 ||
                    r.CacheWriteMultiplier is < 0 or > 100 || r.LongThreshold < 0)
                    throw new InvalidOperationException("Hay una tarifa personalizada no válida.");
                entries[key] = r;
            }
            catalog.HasOverrides = custom.Count > 0;
        }
        return catalog;
    }
    public decimal? Cost(UsageEvent e)
    {
        string model = e.Model.ToLowerInvariant();
        if (model.StartsWith("openai/", StringComparison.Ordinal)) model = model[7..];
        model = Regex.Replace(model, @"-\d{4}-?\d{2}-?\d{2}$", "", RegexOptions.CultureInvariant);
        bool fastAlias = model.EndsWith("-fast", StringComparison.Ordinal);
        if (fastAlias) model = model[..^5];
        model = Regex.Replace(model, @"-\d{4}-?\d{2}-?\d{2}$", "", RegexOptions.CultureInvariant);
        // Compatibility aliases from the pinned upstream. Visible names are never rewritten.
        if (model == "gpt-reserve" || (model == "codex-auto-review" && e.At >= new DateTimeOffset(2026, 7, 9, 0, 0, 0, TimeSpan.Zero))) model = "gpt-5.6-luna";
        if (!rates.TryGetValue(model, out var rate)) return null;
        decimal tier = e.Tier switch
        {
            "standard" or "default" or "auto" or "" => 1,
            "fast" or "priority" => rate.FastMultiplier,
            "flex" or "batch" => 0.5m,
            _ => -1
        };
        if (tier < 0) return null;
        if (fastAlias) tier = rate.FastMultiplier; // Apply once, not twice.
        bool large = rate.LongThreshold > 0 && e.Tokens.Input > rate.LongThreshold;
        decimal inputFactor = large ? 2 : 1, outputFactor = large ? 1.5m : 1;
        var t = e.Tokens;
        long cached = Math.Clamp(t.Cached, 0, t.Input);
        long write = Math.Clamp(t.CacheWrite, 0, t.Input - cached);
        return ((t.Input - cached - write) * rate.Input * inputFactor + cached * rate.Cached * inputFactor +
            write * rate.Input * rate.CacheWriteMultiplier * inputFactor + t.Output * rate.Output * outputFactor) * tier / 1_000_000m;
    }
}

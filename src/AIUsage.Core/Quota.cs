// Duration-based window mapping adapted from OpenUsage v0.7.12 (MIT).
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AIUsage.Core;

public static class QuotaParser
{
    public static QuotaSnapshot Parse(JsonElement body, DateTimeOffset at, string source)
    {
        var windows = new List<LimitWindow>();
        var rates = body.Get("rate_limit");
        string id = body.Text("rate_limit_id") ?? "codex";
        string prefix = id.Contains("spark", StringComparison.OrdinalIgnoreCase) ? "Spark" : id == "codex" ? "Codex" : id;
        Add(rates.ValueKind == JsonValueKind.Object ? rates : body, prefix, windows, at);
        var additional = body.Get("additional_rate_limits");
        if (additional.ValueKind == JsonValueKind.Array)
            foreach (var item in additional.EnumerateArray())
            {
                var name = item.Text("limit_name") ?? item.Text("rate_limit_name") ?? item.Text("rate_limit_id") ?? "Adicional";
                if (name.Contains("spark", StringComparison.OrdinalIgnoreCase)) name = "Spark";
                var rate = item.Get("rate_limit");
                Add(rate.ValueKind == JsonValueKind.Object ? rate : item, name, windows, at);
            }
        var credits = body.Get("credits");
        var balance = credits.Get("balance");
        string? creditText = balance.ValueKind == JsonValueKind.String ? balance.GetString() : balance.Number()?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (credits.Get("unlimited").ValueKind == JsonValueKind.True) creditText = "Sin límite";
        var resets = body.Get("rate_limit_reset_credits").Get("available_count").Number();
        return new(at, source, body.Text("plan_type"), windows, creditText, resets.HasValue ? (long)Math.Max(0, resets.Value) : null);
    }
    private static void Add(JsonElement limits, string prefix, List<LimitWindow> result, DateTimeOffset at)
    {
        foreach (var (key, fallback) in new[] { ("primary", "Sesión"), ("secondary", "Semanal") })
        {
            var window = limits.Get(key + "_window");
            if (window.ValueKind != JsonValueKind.Object) window = limits.Get(key);
            var used = window.Get("used_percent").Number();
            if (!used.HasValue) continue; // Missing is unknown, never zero.
            var seconds = window.Get("limit_window_seconds").Number();
            var minutes = window.Get("window_minutes").Number();
            seconds ??= minutes.HasValue ? minutes * 60 : null;
            string name = seconds switch { >= 518400 and <= 691200 => "Semanal", >= 14400 and <= 21600 => "Sesión", > 0 => $"{seconds / 3600:0.#} h", _ => fallback };
            var reset = window.Get("reset_at").Date() ?? window.Get("resets_at").Date();
            var after = window.Get("reset_after_seconds").Number();
            if (reset is null && after is >= 0 and < 315360000) reset = at.AddSeconds(after.Value);
            result.Add(new($"{prefix} · {name}", used.Value, reset, seconds.HasValue ? (long)seconds.Value : null));
        }
    }
}

public sealed class CodexUsageClient : IDisposable
{
    // Read-only internal endpoint used by OpenUsage. It is not a guaranteed public API.
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = 1_048_576 };
    public async Task<QuotaSnapshot> ReadAsync(string home, CancellationToken ct)
    {
        string path = Path.Combine(home, "auth.json");
        if (!File.Exists(path)) throw new InvalidOperationException("No hay auth.json en esta carpeta. Inicia sesión en Codex. Las credenciales del almacén de Windows no se leen en v0.1.");
        if (new FileInfo(path).Length > 1_048_576) throw new InvalidOperationException("auth.json es demasiado grande; no se ha leído.");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var auth = await JsonDocument.ParseAsync(file, cancellationToken: ct);
        var tokens = auth.RootElement.Get("tokens");
        var access = tokens.Text("access_token");
        if (string.IsNullOrWhiteSpace(access)) throw new InvalidOperationException("Se necesita la sesión de ChatGPT en Codex; una clave API no permite consultar estos límites.");
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/wham/usage");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        req.Headers.UserAgent.ParseAdd("AIUsage-Windows/0.1.0");
        var account = tokens.Text("account_id");
        if (!string.IsNullOrWhiteSpace(account)) req.Headers.Add("ChatGPT-Account-Id", account);
        using var response = await http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Codex debe renovar su sesión. Abre Codex y vuelve a actualizar. AIUsage no modifica ni renueva tus credenciales.");
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("OpenAI ha limitado las consultas. Espera unos minutos.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"No se pudieron leer los límites (HTTP {(int)response.StatusCode}).");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return QuotaParser.Parse(doc.RootElement, DateTimeOffset.Now, "Cuenta · consulta online");
    }
    public void Dispose() => http.Dispose();
}

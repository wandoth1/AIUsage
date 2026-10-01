using static AIUsage.Core.L10n;
// Duration-based window mapping adapted from OpenUsage v0.7.12 (MIT).
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AIUsage.Core;

public static class QuotaParser
{
    public static QuotaSnapshot Parse(JsonElement body, DateTimeOffset at, string source, string? accountKey = null)
    {
        var windows = new List<LimitWindow>();
        AddProvider(body, windows, at, "codex");
        var additional = body.Get("additional_rate_limits");
        if (additional.ValueKind == JsonValueKind.Array)
            foreach (var item in additional.EnumerateArray().Take(100)) AddProvider(item, windows, at, "additional");
        var credits = body.Get("credits");
        var balance = credits.Get("balance");
        string? creditText = balance.ValueKind == JsonValueKind.String ? balance.GetString() : balance.Number()?.ToString(CultureInfo.InvariantCulture);
        if (credits.Get("unlimited").ValueKind == JsonValueKind.True) creditText = "Unlimited";
        var resets = body.Get("rate_limit_reset_credits").Get("available_count").Number();
        return new(at, source, body.Text("plan_type"), windows, creditText,
            resets.HasValue ? (long)Math.Clamp(resets.Value, 0, 1_000_000_000_000_000d) : null, accountKey);
    }
    private static void AddProvider(JsonElement body, List<LimitWindow> result, DateTimeOffset at, string fallback)
    {
        string? suppliedId = body.Text("limit_id") ?? body.Text("rate_limit_id");
        string? suppliedName = body.Text("limit_name") ?? body.Text("rate_limit_name");
        string id = (suppliedId ?? suppliedName ?? fallback).Trim().ToLowerInvariant();
        if (id.Length == 0) id = fallback;
        string prefix = (suppliedName ?? id).Contains("spark", StringComparison.OrdinalIgnoreCase) || id == "codex_bengalfox"
            ? "Spark" : id == "codex" ? "Codex" : suppliedName ?? id;
        var rates = body.Get("rate_limit");
        Add(rates.ValueKind == JsonValueKind.Object ? rates : body, id, prefix, result, at);
    }
    private static void Add(JsonElement limits, string id, string prefix, List<LimitWindow> result, DateTimeOffset at)
    {
        foreach (var (key, fallback) in new[] { ("primary", "Session"), ("secondary", "Weekly") })
        {
            var window = limits.Get(key + "_window");
            if (window.ValueKind != JsonValueKind.Object) window = limits.Get(key);
            var used = window.Get("used_percent").Number();
            if (!used.HasValue) continue; // Missing is unknown, never zero.
            var seconds = window.Get("limit_window_seconds").Number();
            var minutes = window.Get("window_minutes").Number();
            seconds ??= minutes.HasValue ? minutes * 60 : null;
            if (seconds is not (> 0 and <= 315360000)) seconds = null;
            string name = seconds switch { >= 518400 and <= 691200 => "Weekly", >= 14400 and <= 21600 => "Session", > 0 => (seconds.Value / 3600).ToString("0.#", CultureInfo.InvariantCulture) + " h", _ => fallback };
            var reset = window.Get("reset_at").Date() ?? window.Get("resets_at").Date();
            var after = window.Get("reset_after_seconds").Number() ?? window.Get("resets_in_seconds").Number();
            if (reset is null && after is >= 0 and < 315360000 && (DateTimeOffset.MaxValue - at).TotalSeconds >= after.Value)
                reset = at.AddSeconds(after.Value);
            // Duration, not the primary/secondary slot or translated label, identifies a window.
            string identity = id + ":" + (seconds?.ToString("R", CultureInfo.InvariantCulture) ?? key);
            result.Add(new($"{prefix} · {name}", used.Value, reset, seconds.HasValue ? (long)seconds.Value : null, identity));
        }
    }
}

public static class QuotaSelection
{
    public static List<(QuotaSnapshot Snapshot, LimitWindow Window)> Select(IEnumerable<QuotaSnapshot> local, QuotaSnapshot? online)
    {
        // Never fill missing online windows with unrelated, older local account limits.
        IEnumerable<QuotaSnapshot> snapshots = online is null ? local : [online];
        return snapshots.SelectMany(q => q.Windows.Select(w => (Snapshot: q, Window: w)))
            .GroupBy(x => (x.Snapshot.AccountKey, Id: string.IsNullOrEmpty(x.Window.Id) ? x.Window.Name : x.Window.Id))
            .Select(g => g.OrderByDescending(x => x.Snapshot.At).First())
            .OrderBy(x => x.Snapshot.AccountKey).ThenBy(x => x.Window.Name).ToList();
    }
}

public sealed class CodexUsageClient : IDisposable
{
    public const string UsageEndpoint = "https://chatgpt.com/backend-api/wham/usage";
    private const int MaxBytes = 1_048_576;
    private readonly HttpClient http;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private int transientFailures;
    public DateTimeOffset NextAllowedAt { get; private set; } = DateTimeOffset.MinValue;
    public CodexUsageClient() : this(new HttpClientHandler { AllowAutoRedirect = false }, TimeProvider.System) { }
    // Injection is for deterministic, offline regression tests; the app always uses the default handler.
    public CodexUsageClient(HttpMessageHandler handler, TimeProvider? clock = null)
    {
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = MaxBytes };
        this.clock = clock ?? TimeProvider.System;
    }
    public async Task<QuotaSnapshot> ReadAsync(string home, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (clock.GetUtcNow() < NextAllowedAt)
                throw new InvalidOperationException(F("QuotaWaiting", NextAllowedAt.ToLocalTime()));
            string path = Path.Combine(home, "auth.json");
            var credentials = await ReadCredentials(path, ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.Access);
            request.Headers.UserAgent.ParseAdd("AIUsage-Windows/" + AppVersion.Value);
            if (credentials.Account is not null) request.Headers.Add("ChatGPT-Account-Id", credentials.Account);
            NextAllowedAt = clock.GetUtcNow().AddMinutes(1);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, deadline.Token);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new InvalidOperationException(T("RenewSession"));
                if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    BackOff(response.Headers.RetryAfter);
                    throw new InvalidOperationException(F("QuotaRejected", (int)response.StatusCode, NextAllowedAt.ToLocalTime()));
                }
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(F("QuotaHttpError", (int)response.StatusCode));
                // Never follow a redirect manually. The default handler also disables redirects.
                var bytes = await response.Content.ReadAsByteArrayAsync(deadline.Token);
                if (bytes.Length > MaxBytes) throw new InvalidOperationException(T("QuotaTooLarge"));
                using var doc = JsonDocument.Parse(bytes);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException(T("QuotaInvalid"));
                var latestCredentials = await ReadCredentials(path, deadline.Token);
                if (latestCredentials != credentials)
                    throw new InvalidOperationException(T("SessionChanged"));
                transientFailures = 0;
                return QuotaParser.Parse(doc.RootElement, clock.GetUtcNow(), "Online account", AccountIdentity.Key(credentials.Account));
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                BackOff(null);
                throw;
            }
        }
        catch (FormatException) { throw new InvalidOperationException(T("AuthInvalidFormat")); }
        finally { gate.Release(); }
    }
    private void BackOff(RetryConditionHeaderValue? retry)
    {
        transientFailures = Math.Min(transientFailures + 1, 6);
        var now = clock.GetUtcNow();
        var next = now.AddSeconds(Math.Min(3600, 60 * Math.Pow(2, transientFailures)));
        if (retry?.Date is { } date && date > next) next = date;
        if (retry?.Delta is { } delta && delta > TimeSpan.Zero && delta <= DateTimeOffset.MaxValue - now && now + delta > next) next = now + delta;
        NextAllowedAt = next;
    }
    private sealed record Credentials(string Access, string? Account);
    private static async Task<Credentials> ReadCredentials(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) throw new InvalidOperationException(T("NoAuth"));
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] bytes = new byte[MaxBytes + 1];
        int count = await file.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellationToken: ct);
        if (count > MaxBytes) throw new InvalidOperationException(T("AuthTooLarge"));
        using var auth = JsonDocument.Parse(bytes.AsMemory(0, count));
        var tokens = auth.RootElement.Get("tokens");
        string? access = tokens.Text("access_token");
        string? account = tokens.Text("account_id");
        // Validate before assigning headers; error messages never contain either value.
        if (string.IsNullOrWhiteSpace(access)) throw new InvalidOperationException(T("NeedChatGPTSession"));
        if (access.Length > 65536 || access.Any(c => c <= 32 || c >= 127) ||
            (account is not null && (account.Length > 4096 || account.Any(c => c <= 32 || c >= 127))))
            throw new InvalidOperationException(T("AuthInvalidCharacters"));
        return new(access, string.IsNullOrEmpty(account) ? null : account);
    }
    public void Dispose() => http.Dispose();
}

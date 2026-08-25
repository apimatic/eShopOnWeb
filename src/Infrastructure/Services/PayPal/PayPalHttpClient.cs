using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

public class PayPalHttpClient
{
    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly IMemoryCache _cache;
    private const string TokenCacheKey = "paypal_access_token";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public PayPalHttpClient(HttpClient http, IOptions<PayPalSettings> settings, IMemoryCache cache)
    {
        _settings = settings.Value;
        _cache = cache;
        _http = http;
        _http.BaseAddress = new Uri(_settings.GetBaseUrl() + "/");
    }

    // ── Token management ──────────────────────────────────────────────────

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && cached != null)
            return cached;

        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));

        var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        var resp = await _http.SendAsync(request, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new PayPalException(
                $"Failed to obtain PayPal access token: {body}", httpStatusCode: (int)resp.StatusCode);

        var token = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions)
                    ?? throw new PayPalException("Empty token response from PayPal.");

        // Cache until 60 s before expiry
        var expiry = TimeSpan.FromSeconds(Math.Max(token.ExpiresIn - 60, 30));
        _cache.Set(TokenCacheKey, token.AccessToken, expiry);
        return token.AccessToken;
    }

    // ── Generic request helpers ───────────────────────────────────────────

    public async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path, TRequest body, string? idempotencyKey = null, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        var json = JsonSerializer.Serialize(body, JsonOptions);

        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (idempotencyKey != null)
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(request, ct);
        return await DeserializeOrThrowAsync<TResponse>(resp, ct);
    }

    public async Task<TResponse> PostEmptyAsync<TResponse>(
        string path, string? idempotencyKey = null, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (idempotencyKey != null)
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(request, ct);
        return await DeserializeOrThrowAsync<TResponse>(resp, ct);
    }

    public async Task PostVoidAsync(string path, string? idempotencyKey = null, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (idempotencyKey != null)
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);

        var resp = await _http.SendAsync(request, ct);
        if (resp.StatusCode == HttpStatusCode.NoContent || resp.IsSuccessStatusCode)
            return;
        var body = await resp.Content.ReadAsStringAsync(ct);
        throw BuildException(body, (int)resp.StatusCode);
    }

    public async Task<TResponse> GetAsync<TResponse>(string path, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _http.SendAsync(request, ct);
        return await DeserializeOrThrowAsync<TResponse>(resp, ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _http.SendAsync(request, ct);
        if (resp.StatusCode == HttpStatusCode.NoContent || resp.IsSuccessStatusCode)
            return;
        var body = await resp.Content.ReadAsStringAsync(ct);
        throw BuildException(body, (int)resp.StatusCode);
    }

    // ── Error handling ────────────────────────────────────────────────────

    private async Task<T> DeserializeOrThrowAsync<T>(HttpResponseMessage resp, CancellationToken ct)
    {
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (resp.IsSuccessStatusCode)
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions)
                   ?? throw new PayPalException("Empty response from PayPal.");
        }
        throw BuildException(body, (int)resp.StatusCode);
    }

    private static PayPalException BuildException(string body, int statusCode)
    {
        try
        {
            var err = JsonSerializer.Deserialize<PayPalErrorResponse>(body, JsonOptions);
            var detail = err?.Details?.FirstOrDefault();
            var msg = err?.Message ?? body;
            if (detail != null)
                msg = $"{msg} — {detail.Issue}: {detail.Description}";
            return new PayPalException(msg, err?.Name, err?.DebugId, statusCode);
        }
        catch
        {
            return new PayPalException(body, httpStatusCode: statusCode);
        }
    }

    // ── Amount formatting ─────────────────────────────────────────────────

    public static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static decimal ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
}

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Low-level transport for PayPal's REST APIs, built to the OpenAPI specs in <c>api-specs/</c>:
/// resolves the base URL, obtains and caches a client-credentials OAuth token
/// (<c>POST /v1/oauth2/token</c>, the token URL declared by every spec's Oauth2 scheme), attaches
/// the bearer token and per-request headers, and turns PayPal error bodies into typed exceptions.
/// </summary>
public class PayPalApiClient
{
    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalApiClient> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public PayPalApiClient(HttpClient httpClient, IOptions<PayPalSettings> settings,
        ILogger<PayPalApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public string Currency => string.IsNullOrWhiteSpace(_settings.Currency) ? "USD" : _settings.Currency.Trim();

    private Uri BuildUri(string relativePath)
        => new(_settings.ResolvedBaseUrl + (relativePath.StartsWith('/') ? relativePath : "/" + relativePath));

    /// <summary>Sends a request and deserializes the JSON response body into <typeparamref name="T"/>.</summary>
    public async Task<T> SendAsync<T>(HttpMethod method, string path, object? body,
        IEnumerable<(string Name, string Value)>? headers, CancellationToken ct)
    {
        var response = await SendRawAsync(method, path, body, headers, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new PayPalApiException($"PayPal returned an empty body for {method} {path}.",
                (int)response.StatusCode);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(content, Json)
                   ?? throw new PayPalApiException($"PayPal returned a null {typeof(T).Name} for {method} {path}.");
        }
        catch (JsonException ex)
        {
            throw new PayPalApiException($"Could not parse PayPal response for {method} {path}: {ex.Message}",
                (int)response.StatusCode, inner: ex);
        }
    }

    /// <summary>Sends a request that is expected to return no content (e.g. void, delete).</summary>
    public async Task SendNoContentAsync(HttpMethod method, string path, object? body,
        IEnumerable<(string Name, string Value)>? headers, CancellationToken ct)
    {
        var response = await SendRawAsync(method, path, body, headers, ct);
        response.Dispose();
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body,
        IEnumerable<(string Name, string Value)>? headers, CancellationToken ct)
    {
        var response = await SendOnceAsync(method, path, body, headers, forceTokenRefresh: false, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Token may have expired mid-flight; refresh once and retry.
            response.Dispose();
            response = await SendOnceAsync(method, path, body, headers, forceTokenRefresh: true, ct);
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowFromErrorResponseAsync(method, path, response, ct);
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, object? body,
        IEnumerable<(string Name, string Value)>? headers, bool forceTokenRefresh, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(forceTokenRefresh, ct);
        using var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (headers != null)
        {
            foreach (var (name, value) in headers)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    request.Headers.TryAddWithoutValidation(name, value);
                }
            }
        }

        if (body != null)
        {
            var payload = JsonSerializer.Serialize(body, body.GetType(), Json);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        }

        return await _httpClient.SendAsync(request, ct);
    }

    private async Task ThrowFromErrorResponseAsync(HttpMethod method, string path, HttpResponseMessage response,
        CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct);
        response.Content.Headers.TryGetValues("Paypal-Debug-Id", out var _);
        string? debugId = response.Headers.TryGetValues("Paypal-Debug-Id", out var ids)
            ? string.Join(",", ids)
            : null;

        string? name = null;
        string message = content;
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.TryGetProperty("name", out var n)) name = n.GetString();
            if (root.TryGetProperty("message", out var m)) message = m.GetString() ?? content;
            if (debugId == null && root.TryGetProperty("debug_id", out var d)) debugId = d.GetString();
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
                && details.GetArrayLength() > 0)
            {
                message += " | details: " + details.GetRawText();
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the raw content as the message.
        }

        _logger.LogError("PayPal {Method} {Path} failed: {Status} {Name} debug_id={DebugId} {Message}",
            method, path, (int)response.StatusCode, name, debugId, message);
        response.Dispose();

        throw new PayPalApiException(
            $"PayPal call {method} {path} failed with {(int)response.StatusCode}: {name} {message}",
            (int)response.StatusCode, debugId, name);
    }

    private async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && _accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (!forceRefresh && _accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
            {
                throw new PayPalApiException(
                    "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                    "(from PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET).");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
            var basic = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _httpClient.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                string? debugId = response.Headers.TryGetValues("Paypal-Debug-Id", out var ids)
                    ? string.Join(",", ids) : null;
                throw new PayPalApiException(
                    $"PayPal token request failed with {(int)response.StatusCode}: {content}",
                    (int)response.StatusCode, debugId);
            }

            var token = JsonSerializer.Deserialize<OAuthTokenResponse>(content, Json);
            if (token == null || string.IsNullOrEmpty(token.AccessToken))
            {
                throw new PayPalApiException("PayPal token response did not contain an access_token.");
            }

            _accessToken = token.AccessToken;
            // Refresh a minute before actual expiry to avoid using a token mid-expiry.
            var lifetime = Math.Max(60, token.ExpiresIn) - 60;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(lifetime);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private sealed class OAuthTokenResponse
    {
        public string? AccessToken { get; set; }
        public string? TokenType { get; set; }
        public int ExpiresIn { get; set; }
    }
}

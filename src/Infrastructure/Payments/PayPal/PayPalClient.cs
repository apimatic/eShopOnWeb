using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

/// <summary>
/// Low-level PayPal REST client, hand-written against the OpenAPI specifications in
/// api-specs/paypal (checkout_orders_v2, payments_payment_v2, vault_payment_tokens_v3,
/// transaction_search_v1). Handles OAuth client-credentials tokens (tokenUrl
/// /v1/oauth2/token from the spec's security scheme), PayPal-Request-Id idempotency
/// headers and PayPal error models. Never logs request bodies, so card data cannot
/// end up in logs.
/// </summary>
public class PayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalSettings> settings, ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public string Currency => _settings.Currency;

    public async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body = null,
        string? idempotencyKey = null, bool preferRepresentation = false, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (preferRepresentation)
        {
            // Prefer header per the spec: ask for the complete resource representation
            // instead of the default minimal (id, status, links) response.
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = response.Content is null ? null : await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            PayPalErrorResponse? error = null;
            if (!string.IsNullOrWhiteSpace(content))
            {
                try { error = JsonSerializer.Deserialize<PayPalErrorResponse>(content, JsonOptions); }
                catch (JsonException) { /* non-JSON error body */ }
            }

            var message = error?.Message ?? $"PayPal request {method} {path} failed with status {(int)response.StatusCode}.";
            if (error?.Details is { Count: > 0 })
            {
                message += " " + string.Join("; ", error.Details.ConvertAll(d => d.Description ?? d.Issue));
            }
            _logger.LogWarning("PayPal {Method} {Path} failed: {Status} {Name} (debug_id {DebugId})",
                method, path, (int)response.StatusCode, error?.Name, error?.DebugId);
            throw new PaymentGatewayException(message, error?.Name, (int)response.StatusCode);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }
        return JsonSerializer.Deserialize<T>(content, JsonOptions);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            // tokenUrl from the Oauth2 security scheme in the PayPal OpenAPI specs.
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal OAuth token request failed with status {Status}", (int)response.StatusCode);
                throw new PaymentGatewayException("Could not authenticate with PayPal.", httpStatusCode: (int)response.StatusCode);
            }

            var token = JsonSerializer.Deserialize<PayPalOAuthTokenResponse>(content, JsonOptions)
                ?? throw new PaymentGatewayException("PayPal returned an empty OAuth token response.");
            _accessToken = token.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn <= 0 ? 300 : token.ExpiresIn - 60);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}

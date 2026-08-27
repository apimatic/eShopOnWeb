using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

/// <summary>
/// Hand-written PayPal REST client built against the OpenAPI specifications in
/// api-specs/paypal (checkout_orders_v2, payments_payment_v2, vault_payment_tokens_v3,
/// transaction_search_v1). Endpoints, schemas, the OAuth2 client-credentials auth
/// scheme (tokenUrl /v1/oauth2/token) and the sandbox server URL all come from those specs.
/// </summary>
public class PayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
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
        _httpClient.BaseAddress = new Uri(_settings.ResolveBaseUrl() + "/");
    }

    public string Currency => _settings.Currency;

    // ----- checkout_orders_v2 -----

    public Task<PayPalOrderResponse> CreateOrderAsync(PayPalOrderRequest request, string requestId,
        CancellationToken cancellationToken)
        => SendAsync<PayPalOrderResponse>(HttpMethod.Post, "v2/checkout/orders", request, requestId, cancellationToken);

    public Task<PayPalOrderResponse> AuthorizeOrderAsync(string orderId, string requestId,
        CancellationToken cancellationToken)
        => SendAsync<PayPalOrderResponse>(HttpMethod.Post, $"v2/checkout/orders/{Uri.EscapeDataString(orderId)}/authorize",
            new { }, requestId, cancellationToken);

    public Task<PayPalOrderResponse> GetOrderAsync(string orderId, CancellationToken cancellationToken)
        => SendAsync<PayPalOrderResponse>(HttpMethod.Get, $"v2/checkout/orders/{Uri.EscapeDataString(orderId)}",
            null, null, cancellationToken);

    // ----- payments_payment_v2 -----

    public Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
        => SendAsync<PayPalAuthorization>(HttpMethod.Get, $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}",
            null, null, cancellationToken);

    public Task<PayPalCapture> CaptureAuthorizationAsync(string authorizationId, PayPalCaptureRequest request,
        string requestId, CancellationToken cancellationToken)
        => SendAsync<PayPalCapture>(HttpMethod.Post, $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            request, requestId, cancellationToken);

    public Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, PayPalReauthorizeRequest request,
        string requestId, CancellationToken cancellationToken)
        => SendAsync<PayPalAuthorization>(HttpMethod.Post, $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            request, requestId, cancellationToken);

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken)
        => await SendAsync<object>(HttpMethod.Post, $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            null, requestId, cancellationToken);

    public Task<PayPalCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
        => SendAsync<PayPalCapture>(HttpMethod.Get, $"v2/payments/captures/{Uri.EscapeDataString(captureId)}",
            null, null, cancellationToken);

    public Task<PayPalRefund> RefundCaptureAsync(string captureId, PayPalRefundRequest? request,
        string requestId, CancellationToken cancellationToken)
        => SendAsync<PayPalRefund>(HttpMethod.Post, $"v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            request, requestId, cancellationToken);

    // ----- vault_payment_tokens_v3 -----

    public Task<PayPalPaymentTokenResponse> CreatePaymentTokenAsync(PayPalPaymentTokenRequest request,
        string requestId, CancellationToken cancellationToken)
        => SendAsync<PayPalPaymentTokenResponse>(HttpMethod.Post, "v3/vault/payment-tokens", request, requestId, cancellationToken);

    public async Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken)
        => await SendAsync<object>(HttpMethod.Delete, $"v3/vault/payment-tokens/{Uri.EscapeDataString(tokenId)}",
            null, null, cancellationToken);

    // ----- transaction_search_v1 -----

    public Task<PayPalTransactionSearchResponse> ListTransactionsAsync(DateTimeOffset startDate, DateTimeOffset endDate,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var query =
            $"v1/reporting/transactions?start_date={Uri.EscapeDataString(ToRfc3339(startDate))}" +
            $"&end_date={Uri.EscapeDataString(ToRfc3339(endDate))}" +
            $"&fields=transaction_info&balance_affecting_records_only=N" +
            $"&page_size={pageSize}&page={page}";
        return SendAsync<PayPalTransactionSearchResponse>(HttpMethod.Get, query, null, null, cancellationToken);
    }

    private static string ToRfc3339(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    // ----- plumbing -----

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(requestId))
        {
            // PayPal-Request-Id: PayPal dedupes retries of the same key (spec: maxLength 108).
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id",
                requestId.Length <= 108 ? requestId : requestId.Substring(0, 108));
        }
        if (body != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        _logger.LogDebug("PayPal {Method} {Path}", method, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            PayPalError? error = null;
            try
            {
                error = JsonSerializer.Deserialize<PayPalError>(content, JsonOptions);
            }
            catch (JsonException) { /* body was not PayPal's error model */ }

            var message = PayPalApiException.Describe(error, response.StatusCode);
            _logger.LogWarning("PayPal {Method} {Path} failed: {Message} (debug id {DebugId})",
                method, path, message, error?.DebugId);
            throw new PayPalApiException(response.StatusCode, error?.Name, message, error?.DebugId);
        }

        if (string.IsNullOrWhiteSpace(content) || typeof(T) == typeof(object))
        {
            return default!;
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new PayPalApiException(response.StatusCode, null,
                $"PayPal returned an empty response for {method} {path}.");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            // OAuth2 client credentials per the spec's security scheme (tokenUrl: /v1/oauth2/token).
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal token request failed with HTTP {StatusCode}.", (int)response.StatusCode);
                throw new PayPalApiException(response.StatusCode, null,
                    $"PayPal rejected the configured credentials (HTTP {(int)response.StatusCode}). Check PayPal:ClientId/PayPal:ClientSecret.");
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(content, JsonOptions)
                ?? throw new PayPalApiException(HttpStatusCode.OK, null, "PayPal returned an empty token response.");
            if (string.IsNullOrEmpty(token.AccessToken))
            {
                throw new PayPalApiException(HttpStatusCode.OK, null, "PayPal returned no access token.");
            }

            _accessToken = token.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn - 60, 30));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}

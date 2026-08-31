using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Payments.Dto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Hand-written PayPal REST client. Every endpoint, parameter and schema is taken from the
/// OpenAPI specifications in api-specs/paypal; the OAuth2 client-credentials flow and token
/// URL come from the specs' securitySchemes section.
///
/// Security note: request and response bodies are never logged because they can carry
/// cardholder data. Only the HTTP method, path, status code and PayPal debug id are logged.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null // property names are explicit via JsonPropertyName
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options, ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // Checkout Orders v2
    // ------------------------------------------------------------------

    public Task<PayPalOrderResponse> CreateOrderAsync(PayPalOrderRequest request, string requestId, CancellationToken cancellationToken = default)
        => SendAsync<PayPalOrderResponse>(HttpMethod.Post, "/v2/checkout/orders", request, requestId, cancellationToken);

    public Task<PayPalOrderResponse> AuthorizeOrderAsync(string orderId, string requestId, CancellationToken cancellationToken = default)
        => SendAsync<PayPalOrderResponse>(HttpMethod.Post, $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}/authorize", body: null, requestId, cancellationToken);

    public Task<PayPalOrderResponse> GetOrderAsync(string orderId, CancellationToken cancellationToken = default)
        => SendAsync<PayPalOrderResponse>(HttpMethod.Get, $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}", body: null, requestId: null, cancellationToken);

    // ------------------------------------------------------------------
    // Payments v2
    // ------------------------------------------------------------------

    public Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
        => SendAsync<PayPalAuthorization>(HttpMethod.Get, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", body: null, requestId: null, cancellationToken);

    public Task<PayPalCapture> CaptureAuthorizationAsync(string authorizationId, PayPalCaptureRequest request, string requestId, CancellationToken cancellationToken = default)
        => SendAsync<PayPalCapture>(HttpMethod.Post, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture", request, requestId, cancellationToken);

    public Task<PayPalAuthorization> ReauthorizeAuthorizationAsync(string authorizationId, PayPalReauthorizeRequest request, string requestId, CancellationToken cancellationToken = default)
        => SendAsync<PayPalAuthorization>(HttpMethod.Post, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize", request, requestId, cancellationToken);

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
        => await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void", body: null, requestId, cancellationToken);

    public Task<PayPalCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken = default)
        => SendAsync<PayPalCapture>(HttpMethod.Get, $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", body: null, requestId: null, cancellationToken);

    public Task<PayPalRefund> RefundCaptureAsync(string captureId, PayPalRefundRequest request, string requestId, CancellationToken cancellationToken = default)
        => SendAsync<PayPalRefund>(HttpMethod.Post, $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund", request, requestId, cancellationToken);

    public Task<PayPalRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken = default)
        => SendAsync<PayPalRefund>(HttpMethod.Get, $"/v2/payments/refunds/{Uri.EscapeDataString(refundId)}", body: null, requestId: null, cancellationToken);

    // ------------------------------------------------------------------
    // Payment Method Tokens (Vault) v3
    // ------------------------------------------------------------------

    public Task<PayPalPaymentTokenResponse> CreatePaymentTokenAsync(PayPalPaymentTokenRequest request, string requestId, CancellationToken cancellationToken = default)
        => SendAsync<PayPalPaymentTokenResponse>(HttpMethod.Post, "/v3/vault/payment-tokens", request, requestId, cancellationToken);

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default)
        => await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}", body: null, requestId: null, cancellationToken);

    // ------------------------------------------------------------------
    // Transaction Search v1
    // ------------------------------------------------------------------

    public Task<PayPalTransactionSearchResponse> SearchTransactionsAsync(DateTimeOffset startDate, DateTimeOffset endDate, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var path = "/v1/reporting/transactions"
            + $"?start_date={Uri.EscapeDataString(startDate.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}"
            + $"&end_date={Uri.EscapeDataString(endDate.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}"
            + $"&page={page}&page_size={pageSize}";
        return SendAsync<PayPalTransactionSearchResponse>(HttpMethod.Get, path, body: null, requestId: null, cancellationToken);
    }

    // ------------------------------------------------------------------
    // Transport
    // ------------------------------------------------------------------

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? requestId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, body, requestId, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return default!;
        }
        return JsonSerializer.Deserialize<T>(content, JsonOptions)!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? requestId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrEmpty(requestId))
        {
            // Idempotency header defined by the specs for POST operations.
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (body != null || method == HttpMethod.Post)
        {
            var json = body == null ? "{}" : JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, cancellationToken);
            var issues = error?.Details?
                .Select(d => string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}")
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!)
                .ToList();
            _logger.LogWarning("PayPal {Method} {Path} failed: {Status} {ErrorName} (debug_id {DebugId})",
                method, path, (int)response.StatusCode, error?.Name, error?.DebugId);
            var message = error?.Message ?? $"PayPal request failed with status {(int)response.StatusCode}.";
            if (issues is { Count: > 0 })
            {
                message += " " + string.Join(" | ", issues);
            }
            throw new PayPalApiException(response.StatusCode, error?.Name, message, error?.DebugId, issues);
        }

        _logger.LogInformation("PayPal {Method} {Path} -> {Status}", method, path, (int)response.StatusCode);
        return response;
    }

    private static async Task<PayPalErrorResponse?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(content)
                ? null
                : JsonSerializer.Deserialize<PayPalErrorResponse>(content, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // OAuth2 client-credentials flow; token endpoint from the specs' securitySchemes
    // (tokenUrl: /v1/oauth2/token). The token is cached until shortly before expiry.
    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken != null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PayPal token request failed: {Status}", (int)response.StatusCode);
                throw new PayPalApiException(response.StatusCode, null,
                    $"PayPal OAuth token request failed with status {(int)response.StatusCode}.", null);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var token = JsonSerializer.Deserialize<PayPalOAuthTokenResponse>(content, JsonOptions)
                ?? throw new PayPalApiException(response.StatusCode, null, "PayPal OAuth token response was empty.", null);

            _accessToken = token.AccessToken;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn - 60));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}

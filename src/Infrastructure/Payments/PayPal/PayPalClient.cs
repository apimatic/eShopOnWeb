using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Payments.PayPal.Dto;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

/// <summary>
/// Hand-written typed client for the PayPal APIs, built strictly against the OpenAPI
/// documents in api-specs/paypal: checkout_orders_v2, payments_payment_v2,
/// vault_payment_tokens_v3 and transaction_search_v1, all authenticated per the specs'
/// Oauth2 client-credentials scheme (tokenUrl /v1/oauth2/token).
/// Request payloads (which may carry card data) are never logged.
/// </summary>
internal sealed class PayPalClient
{
    private const int MaxAttempts = 2;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<PayPalClient> _logger;

    public PayPalClient(HttpClient httpClient, ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    // ---- checkout_orders_v2 ----

    public Task<PayPalOrderResponse> CreateOrderAsync(PayPalCreateOrderRequest request, string requestId,
        CancellationToken cancellationToken)
        => SendAsync<PayPalOrderResponse>(HttpMethod.Post, "/v2/checkout/orders", request, requestId, cancellationToken);

    public Task<PayPalOrderResponse> AuthorizeOrderAsync(string orderId, string requestId, CancellationToken cancellationToken)
        => SendAsync<PayPalOrderResponse>(HttpMethod.Post, $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}/authorize",
            body: new { }, requestId, cancellationToken);

    // ---- payments_payment_v2 ----

    public Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
        => SendAsync<PayPalAuthorization>(HttpMethod.Get, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}",
            body: null, requestId: null, cancellationToken);

    public Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, PayPalReauthorizeRequest request,
        string requestId, CancellationToken cancellationToken)
        => SendAsync<PayPalAuthorization>(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize", request, requestId, cancellationToken);

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken)
        => await SendAsync<PayPalAuthorization>(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void", body: null, requestId, cancellationToken);

    public Task<PayPalCapture> CaptureAuthorizationAsync(string authorizationId, PayPalCaptureRequest request,
        string requestId, CancellationToken cancellationToken)
        => SendAsync<PayPalCapture>(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture", request, requestId, cancellationToken);

    public Task<PayPalCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
        => SendAsync<PayPalCapture>(HttpMethod.Get, $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}",
            body: null, requestId: null, cancellationToken);

    public Task<PayPalRefund> RefundCaptureAsync(string captureId, PayPalRefundRequest request, string requestId,
        CancellationToken cancellationToken)
        => SendAsync<PayPalRefund>(HttpMethod.Post, $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            request, requestId, cancellationToken);

    // ---- vault_payment_tokens_v3 ----

    public Task<PayPalPaymentTokenResponse> CreatePaymentTokenAsync(PayPalCreatePaymentTokenRequest request,
        string requestId, CancellationToken cancellationToken)
        => SendAsync<PayPalPaymentTokenResponse>(HttpMethod.Post, "/v3/vault/payment-tokens", request, requestId, cancellationToken);

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}");
        using var response = await SendWithRetryAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildErrorAsync(response, cancellationToken);
        }
    }

    // ---- transaction_search_v1 ----

    /// <summary>Lists every transaction in the range, following pagination to the last page.</summary>
    public async Task<IReadOnlyList<PayPalTransactionDetail>> ListAllTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        const int pageSize = 500; // spec maximum for page_size
        var results = new List<PayPalTransactionDetail>();
        var page = 1;
        while (true)
        {
            var path = new StringBuilder("/v1/reporting/transactions")
                .Append("?start_date=").Append(Uri.EscapeDataString(FormatInstant(from)))
                .Append("&end_date=").Append(Uri.EscapeDataString(FormatInstant(to)))
                .Append("&fields=transaction_info")
                .Append("&balance_affecting_records_only=N")
                .Append("&page_size=").Append(pageSize)
                .Append("&page=").Append(page)
                .ToString();

            var response = await SendAsync<PayPalTransactionSearchResponse>(HttpMethod.Get, path, body: null,
                requestId: null, cancellationToken);

            if (response.TransactionDetails is not null)
            {
                results.AddRange(response.TransactionDetails);
            }

            if (page >= response.TotalPages || response.TransactionDetails is null || response.TransactionDetails.Count == 0)
            {
                break;
            }
            page++;
        }
        return results;
    }

    private static string FormatInstant(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    // ---- plumbing ----

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(method, path, body, requestId, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildErrorAsync(response, cancellationToken);
        }
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content is null)
        {
            return default!;
        }
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return default!;
        }
        return JsonSerializer.Deserialize<T>(content, JsonOptions)
               ?? throw new PayPalApiException(response.StatusCode, null, "PayPal returned an unreadable response.");
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            if (requestId is not null)
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }
            if (body is not null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            }

            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < MaxAttempts)
            {
                response.Dispose();
                continue;
            }
            return response;
        }
        return response!;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => await _httpClient.SendAsync(request, cancellationToken);

    private async Task<PayPalApiException> BuildErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? raw = null;
        PayPalErrorResponse? error = null;
        try
        {
            raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                error = JsonSerializer.Deserialize<PayPalErrorResponse>(raw, JsonOptions);
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with the raw text.
        }

        _logger.LogWarning("PayPal call failed with status {StatusCode}, error {ErrorName}, debug id {DebugId}",
            (int)response.StatusCode, error?.Name, error?.DebugId);
        return new PayPalApiException(response.StatusCode, error, raw?.Length > 500 ? raw[..500] : raw);
    }
}

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Thin, spec-faithful client over the PayPal REST endpoints this integration uses:
/// Checkout Orders v2 (create + capture), Payments v2 (refund) and Vault Payment Tokens v3.
/// It handles bearer auth, the <c>PayPal-Request-Id</c> idempotency header and error translation.
/// It deliberately never logs request bodies, which may contain card data.
/// </summary>
internal sealed class PayPalApiClient
{
    private readonly HttpClient _httpClient;
    private readonly PayPalAccessTokenProvider _tokenProvider;
    private readonly ILogger<PayPalApiClient> _logger;

    public PayPalApiClient(HttpClient httpClient, PayPalAccessTokenProvider tokenProvider, ILogger<PayPalApiClient> logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    /// <summary>POST /v2/checkout/orders — create an order with a card payment source and capture it.</summary>
    public Task<OrderResponse> CreateOrderAsync(OrderRequest body, string requestId, CancellationToken cancellationToken)
        => SendAsync<OrderRequest, OrderResponse>(HttpMethod.Post, "/v2/checkout/orders", body, requestId, preferRepresentation: true, cancellationToken);

    /// <summary>POST /v2/checkout/orders/{id}/capture — capture a previously created, approved order.</summary>
    public Task<OrderResponse> CaptureOrderAsync(string orderId, string requestId, CancellationToken cancellationToken)
        => SendAsync<object, OrderResponse>(HttpMethod.Post, $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}/capture", new object(), requestId, preferRepresentation: true, cancellationToken);

    /// <summary>POST /v2/payments/captures/{capture_id}/refund — refund a capture in full (empty body).</summary>
    public Task<RefundResponse> RefundCaptureAsync(string captureId, string requestId, CancellationToken cancellationToken)
        => SendAsync<object, RefundResponse>(HttpMethod.Post, $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund", new object(), requestId, preferRepresentation: true, cancellationToken);

    /// <summary>POST /v3/vault/payment-tokens — vault a card and return its permanent token.</summary>
    public Task<VaultPaymentTokenResponse> CreatePaymentTokenAsync(VaultPaymentTokenRequest body, string requestId, CancellationToken cancellationToken)
        => SendAsync<VaultPaymentTokenRequest, VaultPaymentTokenResponse>(HttpMethod.Post, "/v3/vault/payment-tokens", body, requestId, preferRepresentation: false, cancellationToken);

    /// <summary>DELETE /v3/vault/payment-tokens/{id} — remove a vaulted card. 404 is treated as already gone.</summary>
    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}", requestId: null, preferRepresentation: false, cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent
            || response.StatusCode == HttpStatusCode.OK
            || response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        await ThrowForErrorAsync(response, "delete vaulted card", cancellationToken);
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        HttpMethod method,
        string path,
        TRequest body,
        string? requestId,
        bool preferRepresentation,
        CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(method, path, requestId, preferRepresentation, cancellationToken);
        request.Content = JsonContent.Create(body, options: PayPalJson.Options);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForErrorAsync(response, $"{method} {path}", cancellationToken);
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<TResponse>(content, PayPalJson.Options);
        if (result is null)
        {
            throw new PayPalApiException($"PayPal returned an unexpected empty response for {path}.");
        }
        return result;
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string path, string? requestId, bool preferRepresentation, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        return request;
    }

    private async Task ThrowForErrorAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        PayPalErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorResponse>(content, PayPalJson.Options);
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with a generic message.
        }

        var debugId = error?.DebugId;
        var detail = BuildErrorMessage(error);

        // Log only PayPal's structured error fields (name/debug_id) — never the request body.
        _logger.LogWarning("PayPal {Operation} failed: status={Status} name={Name} debugId={DebugId}",
            operation, status, error?.Name, debugId);

        throw new PayPalApiException(
            $"PayPal request failed ({status}){(detail is null ? "." : $": {detail}")}",
            status,
            debugId);
    }

    private static string? BuildErrorMessage(PayPalErrorResponse? error)
    {
        if (error is null)
        {
            return null;
        }

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(error.Name))
        {
            sb.Append(error.Name);
        }
        if (!string.IsNullOrEmpty(error.Message))
        {
            sb.Append(sb.Length > 0 ? " - " : string.Empty).Append(error.Message);
        }
        if (error.Details is { Count: > 0 })
        {
            var first = error.Details[0];
            var issue = first.Issue ?? first.Description;
            if (!string.IsNullOrEmpty(issue))
            {
                sb.Append(" (").Append(issue).Append(')');
            }
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }
}

using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal gateway implemented directly against the OpenAPI specs under api-specs/paypal/:
/// Checkout Orders v2 (create + capture), Payments v2 (refund) and Vault Payment Tokens v3.
///
/// Card details flow straight through to PayPal and are never logged or persisted here.
/// </summary>
public sealed class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPayPalAccessTokenProvider _tokenProvider;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(
        IHttpClientFactory httpClientFactory,
        IPayPalAccessTokenProvider tokenProvider,
        ILogger<PayPalGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public Task<PayPalChargeResult> ChargeWithCardAsync(Money amount, CardPaymentDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var orderRequest = new CreateOrderRequest
        {
            Intent = "CAPTURE",
            PurchaseUnits = new()
            {
                new PurchaseUnitRequest { Amount = ToAmount(amount) }
            },
            PaymentSource = new OrderPaymentSourceRequest
            {
                Card = new OrderCardRequest
                {
                    Number = card.Number,
                    Expiry = NormalizeExpiry(card.Expiry),
                    SecurityCode = card.SecurityCode,
                    Name = card.Name,
                    BillingAddress = ToBillingAddress(card.BillingAddress)
                }
            }
        };

        return CreateAndCaptureOrderAsync(orderRequest, idempotencyKey, cancellationToken);
    }

    public Task<PayPalChargeResult> ChargeWithVaultedCardAsync(Money amount, string vaultToken, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var orderRequest = new CreateOrderRequest
        {
            Intent = "CAPTURE",
            PurchaseUnits = new()
            {
                new PurchaseUnitRequest { Amount = ToAmount(amount) }
            },
            PaymentSource = new OrderPaymentSourceRequest
            {
                Card = new OrderCardRequest { VaultId = vaultToken }
            }
        };

        return CreateAndCaptureOrderAsync(orderRequest, idempotencyKey, cancellationToken);
    }

    private async Task<PayPalChargeResult> CreateAndCaptureOrderAsync(CreateOrderRequest orderRequest, string idempotencyKey, CancellationToken cancellationToken)
    {
        // POST /v2/checkout/orders  (intent=CAPTURE). Prefer=representation so captures come back inline.
        var created = await SendAsync<OrderResponse>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            orderRequest,
            idempotencyKey,
            preferRepresentation: true,
            cancellationToken);

        var order = created.Body ?? throw new PayPalApiException("PayPal returned an empty order response.");
        var payPalOrderId = order.Id ?? throw new PayPalApiException("PayPal order response did not contain an id.");

        // For a card order with intent=CAPTURE PayPal usually completes synchronously (status
        // COMPLETED, capture inline). If it comes back APPROVED, capture explicitly.
        var capture = FindCapture(order);
        if (order.Status == "COMPLETED" && capture is not null)
        {
            return BuildChargeResult(payPalOrderId, capture);
        }

        if (order.Status == "APPROVED" || capture is null)
        {
            var captured = await SendAsync<OrderResponse>(
                HttpMethod.Post,
                $"/v2/checkout/orders/{payPalOrderId}/capture",
                body: null,
                idempotencyKey + "-cap",
                preferRepresentation: true,
                cancellationToken);

            var capturedOrder = captured.Body ?? throw new PayPalApiException("PayPal returned an empty capture response.");
            capture = FindCapture(capturedOrder);
            if (capture is null)
            {
                throw new PaymentException($"PayPal did not capture the payment (order status '{capturedOrder.Status}').");
            }

            return BuildChargeResult(payPalOrderId, capture);
        }

        // e.g. PAYER_ACTION_REQUIRED - additional (interactive) authentication needed, unsupported here.
        throw new PaymentException($"PayPal could not complete the payment (order status '{order.Status}').");
    }

    private static CaptureResponse? FindCapture(OrderResponse order)
    {
        return order.PurchaseUnits?
            .FirstOrDefault(pu => pu.Payments?.Captures is { Count: > 0 })?
            .Payments?.Captures?
            .FirstOrDefault();
    }

    private PayPalChargeResult BuildChargeResult(string payPalOrderId, CaptureResponse capture)
    {
        var captureId = capture.Id ?? throw new PayPalApiException("PayPal capture did not contain an id.");
        var status = capture.Status ?? "UNKNOWN";

        // COMPLETED = money captured; PENDING = accepted, settling asynchronously. Anything else is a failure.
        if (status is not ("COMPLETED" or "PENDING"))
        {
            throw new PaymentException($"The card payment was not successful (capture status '{status}').");
        }

        return new PayPalChargeResult(payPalOrderId, captureId, status);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // POST /v2/payments/captures/{capture_id}/refund with an empty body = full refund.
        var refunded = await SendAsync<RefundResponse>(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            body: new { },
            idempotencyKey,
            preferRepresentation: true,
            cancellationToken);

        var refund = refunded.Body ?? throw new PayPalApiException("PayPal returned an empty refund response.");
        var refundId = refund.Id ?? throw new PayPalApiException("PayPal refund did not contain an id.");
        var status = refund.Status ?? "UNKNOWN";

        if (status is "FAILED" or "CANCELLED")
        {
            throw new PaymentException($"The refund was not successful (refund status '{status}').");
        }

        return new PayPalRefundResult(refundId, status);
    }

    public async Task<VaultedCard> VaultCardAsync(CardPaymentDetails card, string? existingCustomerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new VaultTokenRequest
        {
            Customer = string.IsNullOrEmpty(existingCustomerId) ? null : new VaultCustomerModel { Id = existingCustomerId },
            PaymentSource = new VaultPaymentSourceRequest
            {
                Card = new VaultCardRequest
                {
                    Name = card.Name,
                    Number = card.Number,
                    Expiry = NormalizeExpiry(card.Expiry),
                    SecurityCode = card.SecurityCode,
                    BillingAddress = ToBillingAddress(card.BillingAddress)
                }
            }
        };

        // POST /v3/vault/payment-tokens
        var response = await SendAsync<VaultTokenResponse>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            request,
            idempotencyKey,
            preferRepresentation: false,
            cancellationToken);

        var token = response.Body ?? throw new PayPalApiException("PayPal returned an empty vault response.");
        var vaultId = token.Id ?? throw new PayPalApiException("PayPal vault response did not contain a token id.");
        var customerId = token.Customer?.Id
            ?? existingCustomerId
            ?? throw new PayPalApiException("PayPal vault response did not contain a customer id.");
        var cardResponse = token.PaymentSource?.Card;

        return new VaultedCard(
            VaultToken: vaultId,
            CustomerId: customerId,
            Last4: cardResponse?.LastDigits,
            Brand: cardResponse?.Brand,
            Expiry: cardResponse?.Expiry,
            CardType: cardResponse?.Type);
    }

    public async Task DeleteVaultedCardAsync(string vaultToken, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(PayPalHttpClient.Name);
        using var request = await BuildRequestAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultToken}", body: null, idempotencyKey: null, preferRepresentation: false, cancellationToken);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new PayPalApiException("Failed to reach PayPal to delete the vaulted card.", ex);
        }

        using (response)
        {
            // 204 = deleted. 404 = already gone; treat as success so delete is idempotent.
            if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            ThrowForError(response.StatusCode, body, "delete the vaulted card");
        }
    }

    // ---- HTTP plumbing ------------------------------------------------------------------

    private sealed record PayPalResponse<T>(T? Body);

    private async Task<PayPalResponse<T>> SendAsync<T>(
        HttpMethod method, string path, object? body, string? idempotencyKey, bool preferRepresentation, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(PayPalHttpClient.Name);
        using var request = await BuildRequestAsync(method, path, body, idempotencyKey, preferRepresentation, cancellationToken);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new PayPalApiException($"Failed to reach PayPal for {method} {path}.", ex);
        }

        using (response)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                ThrowForError(response.StatusCode, responseBody, $"{method} {path}");
            }

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return new PayPalResponse<T>(default);
            }

            var parsed = JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
            return new PayPalResponse<T>(parsed);
        }
    }

    private async Task<HttpRequestMessage> BuildRequestAsync(
        HttpMethod method, string path, object? body, string? idempotencyKey, bool preferRepresentation, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);

        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }

        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, body.GetType(), JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    /// <summary>
    /// Maps a PayPal error response to a domain exception. 5xx / auth failures become
    /// <see cref="PayPalApiException"/> (our fault / upstream); other 4xx (declines, validation)
    /// become <see cref="PaymentException"/> (caller-facing). Never logs card data.
    /// </summary>
    private void ThrowForError(HttpStatusCode statusCode, string responseBody, string operation)
    {
        PayPalErrorResponse? error = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(responseBody))
            {
                error = JsonSerializer.Deserialize<PayPalErrorResponse>(responseBody, JsonOptions);
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with a generic message.
        }

        var issue = error?.Details?.FirstOrDefault()?.Issue;
        var name = error?.Name;
        var debugId = error?.DebugId;

        _logger.LogWarning(
            "PayPal {Operation} failed: status={Status} name={Name} issue={Issue} debugId={DebugId}",
            operation, (int)statusCode, name, issue, debugId);

        var detail = new StringBuilder();
        if (!string.IsNullOrEmpty(name)) detail.Append(name);
        if (!string.IsNullOrEmpty(issue)) detail.Append(detail.Length > 0 ? $" ({issue})" : issue);
        var summary = detail.Length > 0 ? detail.ToString() : $"HTTP {(int)statusCode}";

        if ((int)statusCode >= 500)
        {
            throw new PayPalApiException($"PayPal is currently unavailable ({summary}).", debugId);
        }

        if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden)
        {
            throw new PayPalApiException($"PayPal rejected the credentials or the request was not authorised ({summary}).", debugId);
        }

        // 400 / 404 / 409 / 422 - a problem with the payment request itself (e.g. declined card).
        throw new PaymentException($"PayPal could not process the payment: {summary}.");
    }

    // ---- mapping helpers ----------------------------------------------------------------

    private static AmountRequest ToAmount(Money amount) => new()
    {
        CurrencyCode = string.IsNullOrWhiteSpace(amount.CurrencyCode) ? "USD" : amount.CurrencyCode,
        Value = amount.Amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static CardBillingAddressModel? ToBillingAddress(CardBillingAddress? address)
    {
        if (address is null) return null;
        return new CardBillingAddressModel
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static readonly Regex YearMonth = new(@"^[0-9]{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);

    /// <summary>
    /// Ensures the card expiry is in the PayPal "YYYY-MM" form. Accepts common alternatives
    /// (MM/YY, MM/YYYY, YYYY/MM) and normalises them; passes an already-correct value through.
    /// </summary>
    internal static string NormalizeExpiry(string expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry)) return expiry;
        var value = expiry.Trim();

        if (YearMonth.IsMatch(value)) return value;

        var parts = value.Split(new[] { '/', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            string? year = null, month = null;
            if (parts[0].Length == 4) { year = parts[0]; month = parts[1]; }        // YYYY-MM / YYYY/MM
            else if (parts[1].Length == 4) { month = parts[0]; year = parts[1]; }    // MM/YYYY
            else if (parts[1].Length == 2) { month = parts[0]; year = "20" + parts[1]; } // MM/YY

            if (year is not null && month is not null && int.TryParse(month, out var m) && m is >= 1 and <= 12)
            {
                return $"{year}-{m:00}";
            }
        }

        // Leave as-is; PayPal will validate and reject with a clear error if it is wrong.
        return value;
    }
}

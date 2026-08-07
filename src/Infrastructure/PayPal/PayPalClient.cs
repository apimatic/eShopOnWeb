using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/>, built strictly against the PayPal OpenAPI
/// specifications under <c>api-specs/paypal/</c>:
/// <list type="bullet">
///   <item>Checkout Orders v2 — create &amp; capture an order (raw card or vaulted card).</item>
///   <item>Payments v2 — refund a captured payment.</item>
///   <item>Vault Payment Tokens v3 — save and delete a card.</item>
/// </list>
/// </summary>
public sealed class PayPalClient : IPaymentGateway
{
    public const string HttpClientName = "PayPal.Api";

    // PayPal-generated customer.merchant_customer_id format constraint from the vault spec.
    private static readonly Regex MerchantCustomerIdPattern = new("^[0-9a-zA-Z-_.^*$@#]+$", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly PayPalAccessTokenProvider _tokenProvider;
    private readonly ILogger<PayPalClient> _logger;
    private readonly string _baseUrl;

    public PayPalClient(
        HttpClient httpClient,
        PayPalAccessTokenProvider tokenProvider,
        IOptions<PayPalOptions> options,
        ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _logger = logger;
        _baseUrl = options.Value.ResolveBaseUrl();
    }

    // ---- Charge a raw card -----------------------------------------------------------------

    public async Task<GatewayPaymentResult> ChargeCardAsync(CardChargeRequest request, CancellationToken cancellationToken = default)
    {
        var createRequest = new CreateOrderRequest
        {
            Intent = "CAPTURE",
            PurchaseUnits = { BuildPurchaseUnit(request.Amount, request.CurrencyCode, request.CustomId, request.Description) },
            PaymentSource = new PaymentSource
            {
                Card = new CardRequest
                {
                    Name = request.Card.CardholderName,
                    Number = request.Card.Number,
                    Expiry = request.Card.ExpiryMonthYear,
                    SecurityCode = request.Card.SecurityCode,
                    BillingAddress = MapBillingAddress(request.Card.BillingAddress)
                }
            }
        };

        return await CreateAndCaptureAsync(createRequest, request.IdempotencyKey, cancellationToken);
    }

    // ---- Charge a vaulted card -------------------------------------------------------------

    public async Task<GatewayPaymentResult> ChargeVaultedCardAsync(VaultedCardChargeRequest request, CancellationToken cancellationToken = default)
    {
        var createRequest = new CreateOrderRequest
        {
            Intent = "CAPTURE",
            PurchaseUnits = { BuildPurchaseUnit(request.Amount, request.CurrencyCode, request.CustomId, request.Description) },
            PaymentSource = new PaymentSource
            {
                Card = new CardRequest { VaultId = request.VaultToken }
            }
        };

        return await CreateAndCaptureAsync(createRequest, request.IdempotencyKey, cancellationToken);
    }

    private async Task<GatewayPaymentResult> CreateAndCaptureAsync(CreateOrderRequest createRequest, string idempotencyKey, CancellationToken cancellationToken)
    {
        var create = await SendAsync<OrderResponse>(
            HttpMethod.Post, "/v2/checkout/orders", createRequest, idempotencyKey, preferRepresentation: true, cancellationToken);

        if (!create.IsSuccess || create.Data is null)
        {
            return new GatewayPaymentResult { Success = false, Status = create.StatusText, ErrorMessage = create.ErrorMessage, DebugId = create.DebugId };
        }

        var order = create.Data;
        var status = order.Status;

        // With payment_source.card + intent=CAPTURE, PayPal typically returns COMPLETED with the
        // capture already present. If instead the order is merely APPROVED/CREATED, capture it.
        if (!string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            {
                return new GatewayPaymentResult
                {
                    Success = false,
                    PayPalOrderId = order.Id,
                    Status = status,
                    ErrorMessage = "The card requires additional buyer authentication (3-D Secure), which this API-only flow does not support."
                };
            }

            var capture = await SendAsync<OrderResponse>(
                HttpMethod.Post, $"/v2/checkout/orders/{order.Id}/capture", body: null, $"{idempotencyKey}-capture", preferRepresentation: true, cancellationToken);

            if (!capture.IsSuccess || capture.Data is null)
            {
                return new GatewayPaymentResult { Success = false, PayPalOrderId = order.Id, Status = capture.StatusText, ErrorMessage = capture.ErrorMessage, DebugId = capture.DebugId };
            }

            order = capture.Data;
        }

        var captured = ExtractCapture(order);
        if (captured is null || string.IsNullOrEmpty(captured.Id))
        {
            return new GatewayPaymentResult
            {
                Success = false,
                PayPalOrderId = order.Id,
                Status = order.Status,
                ErrorMessage = "PayPal did not return a capture for the order."
            };
        }

        var captureCompleted = string.Equals(captured.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase);
        if (!captureCompleted)
        {
            return new GatewayPaymentResult
            {
                Success = false,
                PayPalOrderId = order.Id,
                CaptureId = captured.Id,
                Status = captured.Status,
                ErrorMessage = $"The card payment was not completed (capture status: {captured.Status})."
            };
        }

        return new GatewayPaymentResult
        {
            Success = true,
            PayPalOrderId = order.Id,
            CaptureId = captured.Id,
            Status = captured.Status
        };
    }

    // ---- Refund ----------------------------------------------------------------------------

    public async Task<GatewayRefundResult> RefundAsync(string captureId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // A full refund is an empty request body per the Payments v2 spec.
        var refund = await SendAsync<RefundResponse>(
            HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body: null, idempotencyKey, preferRepresentation: true, cancellationToken);

        if (!refund.IsSuccess || refund.Data is null || string.IsNullOrEmpty(refund.Data.Id))
        {
            return new GatewayRefundResult { Success = false, Status = refund.StatusText, ErrorMessage = refund.ErrorMessage, DebugId = refund.DebugId };
        }

        var completed = string.Equals(refund.Data.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(refund.Data.Status, "PENDING", StringComparison.OrdinalIgnoreCase);
        if (!completed)
        {
            return new GatewayRefundResult
            {
                Success = false,
                RefundId = refund.Data.Id,
                Status = refund.Data.Status,
                ErrorMessage = $"The refund was not completed (status: {refund.Data.Status})."
            };
        }

        return new GatewayRefundResult { Success = true, RefundId = refund.Data.Id, Status = refund.Data.Status };
    }

    // ---- Vault a card ----------------------------------------------------------------------

    public async Task<GatewayVaultResult> VaultCardAsync(VaultCardRequest request, CancellationToken cancellationToken = default)
    {
        var tokenRequest = new PaymentTokenRequest
        {
            Customer = BuildCustomer(request.CustomerReference),
            PaymentSource = new VaultPaymentSource
            {
                Card = new VaultCard
                {
                    Name = request.Card.CardholderName,
                    Number = request.Card.Number,
                    Expiry = request.Card.ExpiryMonthYear,
                    SecurityCode = request.Card.SecurityCode,
                    BillingAddress = MapBillingAddress(request.Card.BillingAddress)
                }
            }
        };

        var result = await SendAsync<PaymentTokenResponse>(
            HttpMethod.Post, "/v3/vault/payment-tokens", tokenRequest, request.IdempotencyKey, preferRepresentation: false, cancellationToken);

        if (!result.IsSuccess || result.Data is null || string.IsNullOrEmpty(result.Data.Id))
        {
            return new GatewayVaultResult { Success = false, ErrorMessage = result.ErrorMessage, DebugId = result.DebugId };
        }

        var card = result.Data.PaymentSource?.Card;
        return new GatewayVaultResult
        {
            Success = true,
            VaultToken = result.Data.Id,
            Last4 = card?.LastDigits,
            Brand = card?.Brand,
            ExpiryMonthYear = card?.Expiry,
            CardholderName = card?.Name
        };
    }

    // ---- Delete a vaulted card -------------------------------------------------------------

    public async Task<bool> DeleteVaultedCardAsync(string vaultToken, CancellationToken cancellationToken = default)
    {
        var result = await SendAsync<object>(
            HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultToken}", body: null, idempotencyKey: null, preferRepresentation: false, cancellationToken);

        // A 404 means the token is already gone at PayPal — treat as success for delete semantics.
        if (!result.IsSuccess && result.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return true;
        }

        if (!result.IsSuccess)
        {
            _logger.LogWarning("PayPal vault delete failed with status {Status}. DebugId: {DebugId}", result.StatusCode, result.DebugId);
        }

        return result.IsSuccess;
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private static PurchaseUnitRequest BuildPurchaseUnit(decimal amount, string currencyCode, string? customId, string? description) => new()
    {
        Amount = new MoneyModel
        {
            CurrencyCode = currencyCode,
            Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
        },
        CustomId = customId,
        Description = description
    };

    private static CardBillingAddressModel? MapBillingAddress(CardBillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }

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

    private static CustomerModel? BuildCustomer(string? customerReference)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            return null;
        }

        // merchant_customer_id must satisfy PayPal's format and length; otherwise omit it rather
        // than risk a 422. The vault token we store locally is what actually links card to shopper.
        var trimmed = customerReference.Length <= 64 ? customerReference : customerReference.Substring(0, 64);
        if (!MerchantCustomerIdPattern.IsMatch(trimmed))
        {
            return null;
        }

        return new CustomerModel { MerchantCustomerId = trimmed };
    }

    private static CaptureResponse? ExtractCapture(OrderResponse order) =>
        order.PurchaseUnits?
            .Where(pu => pu.Payments?.Captures is { Count: > 0 })
            .SelectMany(pu => pu.Payments!.Captures!)
            .FirstOrDefault();

    private async Task<PayPalResponse<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? idempotencyKey,
        bool preferRepresentation,
        CancellationToken cancellationToken)
    {
        var accessToken = await _tokenProvider.GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, $"{_baseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (preferRepresentation)
        {
            // Ask PayPal to return the full resource (so captures/refund details are present).
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, mediaType: null, PayPalJson.Options);
        }
        else if (method == HttpMethod.Post)
        {
            // PayPal requires an application/json content type even for empty-bodied POSTs such as a
            // full refund or an order capture; without it the API responds 415 Unsupported Media Type.
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            T? data = default;
            if (!string.IsNullOrWhiteSpace(raw) && typeof(T) != typeof(object))
            {
                data = JsonSerializer.Deserialize<T>(raw, PayPalJson.Options);
            }
            return PayPalResponse<T>.Ok((int)response.StatusCode, data);
        }

        var error = TryParseError(raw);
        var message = BuildErrorMessage(error, (int)response.StatusCode);
        _logger.LogWarning("PayPal {Method} {Path} failed: {Status}. DebugId: {DebugId}. {Message}",
            method, path, (int)response.StatusCode, error?.DebugId ?? "n/a", message);

        return PayPalResponse<T>.Fail((int)response.StatusCode, response.StatusCode.ToString(), message, error?.DebugId);
    }

    private static PayPalErrorResponse? TryParseError(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<PayPalErrorResponse>(raw, PayPalJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string BuildErrorMessage(PayPalErrorResponse? error, int statusCode)
    {
        if (error is null)
        {
            return $"PayPal request failed with status {statusCode}.";
        }

        var detail = error.Details is { Count: > 0 }
            ? $" {error.Details[0].Issue}: {error.Details[0].Description}"
            : string.Empty;

        var message = string.IsNullOrEmpty(error.Message) ? $"PayPal request failed with status {statusCode}." : error.Message;
        return $"{message}{detail}".Trim();
    }

    private sealed class PayPalResponse<T>
    {
        public bool IsSuccess { get; private init; }
        public int StatusCode { get; private init; }
        public string? StatusText { get; private init; }
        public T? Data { get; private init; }
        public string? ErrorMessage { get; private init; }
        public string? DebugId { get; private init; }

        public static PayPalResponse<T> Ok(int statusCode, T? data) =>
            new() { IsSuccess = true, StatusCode = statusCode, Data = data };

        public static PayPalResponse<T> Fail(int statusCode, string statusText, string errorMessage, string? debugId) =>
            new() { IsSuccess = false, StatusCode = statusCode, StatusText = statusText, ErrorMessage = errorMessage, DebugId = debugId };
    }
}

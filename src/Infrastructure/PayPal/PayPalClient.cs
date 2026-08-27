using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal REST client covering OAuth, Orders v2 (authorize intent with direct card or vaulted
/// card), Payments v2 (capture / reauthorize / void / refund), Vault v3 (payment tokens) and
/// Transaction Search v1 (reporting). Contract verified against the official PayPal API
/// references and the paypal-rest-api-specifications OpenAPI repo.
/// Card numbers pass through to PayPal only; they are never logged or persisted here.
/// </summary>
public class PayPalClient : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                "(from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables) via user-secrets.");
        }

        _httpClient.BaseAddress = new Uri(_settings.ResolveBaseUrl() + "/");
    }

    // ------------------------------------------------------------------
    // IPaymentGateway
    // ------------------------------------------------------------------

    public Task<GatewayAuthorizationResult> AuthorizeCardAsync(CardDetails card, decimal amount, string currency,
        string idempotencyKey, string invoiceId, CancellationToken cancellationToken = default)
    {
        var paymentSource = new
        {
            card = new
            {
                number = card.Number,
                expiry = card.Expiry,
                security_code = card.SecurityCode,
                name = card.CardholderName,
                billing_address = ToPayPalAddress(card.BillingAddress)
            }
        };
        return AuthorizeWithPaymentSourceAsync(paymentSource, amount, currency, idempotencyKey, invoiceId, cancellationToken);
    }

    public Task<GatewayAuthorizationResult> AuthorizeVaultedCardAsync(string vaultTokenId, decimal amount, string currency,
        string idempotencyKey, string invoiceId, CancellationToken cancellationToken = default)
    {
        var paymentSource = new
        {
            card = new
            {
                vault_id = vaultTokenId,
                stored_credential = new
                {
                    payment_initiator = "CUSTOMER",
                    payment_type = "ONE_TIME"
                }
            }
        };
        return AuthorizeWithPaymentSourceAsync(paymentSource, amount, currency, idempotencyKey, invoiceId, cancellationToken);
    }

    public async Task<GatewayAuthorizationInfo> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var auth = await SendAsync<PayPalAuthorization>(HttpMethod.Get,
            $"v2/payments/authorizations/{authorizationId}", body: null, idempotencyKey: null, cancellationToken);
        return ToAuthorizationInfo(auth);
    }

    public async Task<GatewayCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, string invoiceId, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            amount = Money(amount, currency),
            invoice_id = invoiceId,
            final_capture = true
        };

        var capture = await SendAsync<PayPalCapture>(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture", request, idempotencyKey, cancellationToken,
            preferRepresentation: true);

        var breakdown = capture.SellerReceivableBreakdown;
        return new GatewayCaptureResult(
            capture.Id ?? string.Empty,
            capture.Status ?? string.Empty,
            ParseMoney(breakdown?.GrossAmount ?? capture.Amount),
            ParseMoneyOrNull(breakdown?.PayPalFee),
            ParseMoneyOrNull(breakdown?.NetAmount),
            (breakdown?.GrossAmount ?? capture.Amount)?.CurrencyCode ?? currency);
    }

    public async Task<GatewayAuthorizationInfo> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new { amount = Money(amount, currency) };
        var auth = await SendAsync<PayPalAuthorization>(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize", request, idempotencyKey, cancellationToken);
        return ToAuthorizationInfo(auth);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void",
            body: null, idempotencyKey, cancellationToken);
    }

    public async Task<GatewayRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, string invoiceId, CancellationToken cancellationToken = default)
    {
        // An empty body refunds the full remaining amount; an amount object makes it partial.
        object? request = amount is null
            ? new { invoice_id = invoiceId, custom_id = idempotencyKey }
            : new { amount = Money(amount.Value, currency), invoice_id = invoiceId, custom_id = idempotencyKey };

        var refund = await SendAsync<PayPalRefund>(HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund", request, idempotencyKey, cancellationToken,
            preferRepresentation: true);

        return new GatewayRefundResult(
            refund.Id ?? string.Empty,
            refund.Status ?? string.Empty,
            ParseMoney(refund.Amount),
            refund.Amount?.CurrencyCode ?? currency);
    }

    public async Task<GatewayVaultTokenResult> CreateVaultTokenAsync(CardDetails card, string merchantCustomerId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            payment_source = new
            {
                card = new
                {
                    number = card.Number,
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    name = card.CardholderName,
                    billing_address = ToPayPalAddress(card.BillingAddress)
                }
            },
            customer = new
            {
                merchant_customer_id = merchantCustomerId
            }
        };

        var token = await SendAsync<PayPalVaultToken>(HttpMethod.Post,
            "v3/vault/payment-tokens", request, idempotencyKey, cancellationToken);

        if (string.Equals(token.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentDeclinedException(
                "PayPal requires a browser-based shopper action to vault this card (PAYER_ACTION_REQUIRED); " +
                "this integration is headless and cannot complete it.");
        }

        var vaultedCard = token.PaymentSource?.Card;
        return new GatewayVaultTokenResult(
            token.Id ?? string.Empty,
            token.Customer?.Id,
            vaultedCard?.Brand,
            vaultedCard?.LastDigits,
            vaultedCard?.Expiry,
            vaultedCard?.Name);
    }

    public async Task DeleteVaultTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"v3/vault/payment-tokens/{vaultTokenId}",
            body: null, idempotencyKey: null, cancellationToken);
    }

    public async Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<GatewayTransaction>();

        // The Transaction Search API supports a maximum range of 31 days per request.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31) < to ? windowStart.AddDays(31) : to;

            var page = 1;
            while (true)
            {
                var path = "v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(ToPayPalDate(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(ToPayPalDate(windowEnd))}" +
                    "&fields=transaction_info&page_size=500" +
                    $"&page={page}";

                var response = await SendAsync<PayPalTransactionSearchResponse>(HttpMethod.Get, path,
                    body: null, idempotencyKey: null, cancellationToken);

                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info?.TransactionId is null) continue;
                        results.Add(new GatewayTransaction(
                            info.TransactionId,
                            info.TransactionEventCode,
                            info.TransactionStatus,
                            ParseMoneyOrNull(info.TransactionAmount),
                            info.TransactionAmount?.CurrencyCode,
                            ParseMoneyOrNull(info.FeeAmount),
                            ParsePayPalDate(info.TransactionUpdatedDate ?? info.TransactionInitiationDate)));
                    }
                }

                if (page >= (response.TotalPages == 0 ? 1 : response.TotalPages)) break;
                page++;
            }

            windowStart = windowEnd;
        }

        return results;
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private async Task<GatewayAuthorizationResult> AuthorizeWithPaymentSourceAsync(object paymentSource,
        decimal amount, string currency, string idempotencyKey, string invoiceId, CancellationToken cancellationToken)
    {
        var createRequest = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    amount = Money(amount, currency),
                    invoice_id = invoiceId,
                    custom_id = invoiceId
                }
            },
            payment_source = paymentSource
        };

        var order = await SendAsync<PayPalOrder>(HttpMethod.Post, "v2/checkout/orders",
            createRequest, idempotencyKey, cancellationToken);

        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentDeclinedException(
                "PayPal requires a browser-based shopper approval for this card (PAYER_ACTION_REQUIRED); " +
                "this integration is headless and cannot complete it.");
        }

        // With a direct card payment source PayPal authorizes during order creation, so the
        // authorization is usually already on the response. Only call the authorize endpoint
        // when the order was merely approved (no authorization yet).
        var authorization = order.PurchaseUnits?.SelectMany(pu => pu.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorization>())
            .FirstOrDefault();

        if (authorization?.Id is null)
        {
            var authorized = await SendAsync<PayPalOrder>(HttpMethod.Post,
                $"v2/checkout/orders/{order.Id}/authorize", body: new { }, idempotencyKey + "-authorize", cancellationToken);

            authorization = authorized.PurchaseUnits?.SelectMany(pu => pu.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorization>())
                .FirstOrDefault();
        }

        if (authorization?.Id is null)
        {
            throw new PaymentGatewayException(
                $"PayPal did not return an authorization for order {order.Id} (status '{order.Status}').");
        }

        if (string.Equals(authorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentDeclinedException($"PayPal denied the authorization for order {order.Id}.");
        }

        return new GatewayAuthorizationResult(
            order.Id ?? string.Empty,
            authorization.Id,
            authorization.Status ?? string.Empty,
            ParseMoney(authorization.Amount),
            authorization.Amount?.CurrencyCode ?? currency,
            ParsePayPalDate(authorization.ExpirationTime));
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

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials"
                })
            };
            var credentials = Convert.ToBase64String(
                System.Text.Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal token request failed with status {StatusCode}.", (int)response.StatusCode);
                throw new PaymentGatewayException(
                    $"PayPal rejected the client credentials (HTTP {(int)response.StatusCode}).", response.StatusCode);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(body, JsonOptions);
            _accessToken = token?.AccessToken
                ?? throw new PaymentGatewayException("PayPal token response did not contain an access token.");
            var expiresIn = token?.ExpiresIn ?? 300;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresIn - 60, 30));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? idempotencyKey,
        CancellationToken cancellationToken, bool preferRepresentation = false)
    {
        using var response = await SendAsync(method, path, body, idempotencyKey, cancellationToken, preferRepresentation);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new PaymentGatewayException($"PayPal returned an empty response for {method} {path}.");
        }
        return JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new PaymentGatewayException($"Could not parse PayPal's response for {method} {path}.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? idempotencyKey,
        CancellationToken cancellationToken, bool preferRepresentation = false)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        // NOTE: never log request/response bodies here — order and vault payloads carry card data.
        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var (name, message, debugId) = ParseError(errorBody);
            _logger.LogWarning("PayPal {Method} {Path} failed: {StatusCode} {ErrorName} (debug {DebugId}).",
                method, path, (int)response.StatusCode, name, debugId);
            response.Dispose();
            throw new PaymentGatewayException(
                $"PayPal {method} {path} failed with HTTP {(int)response.StatusCode}: {name} - {message}",
                response.StatusCode, name);
        }

        return response;
    }

    private static (string? Name, string? Message, string? DebugId) ParseError(string body)
    {
        try
        {
            var error = JsonSerializer.Deserialize<PayPalError>(body, JsonOptions);
            var issue = error?.Details?.FirstOrDefault()?.Issue;
            var message = error?.Message ?? issue ?? "unknown error";
            return (error?.Name, issue is null ? message : $"{message} ({issue})", error?.DebugId);
        }
        catch (JsonException)
        {
            return (null, "unparseable error response", null);
        }
    }

    private static object? ToPayPalAddress(BillingAddress? address) => address is null
        ? null
        : new
        {
            address_line_1 = address.AddressLine1,
            address_line_2 = address.AddressLine2,
            admin_area_2 = address.City,
            admin_area_1 = address.State,
            postal_code = address.PostalCode,
            country_code = address.CountryCode
        };

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency,
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static decimal ParseMoney(PayPalMoney? money) =>
        money?.Value is null ? 0m : decimal.Parse(money.Value, CultureInfo.InvariantCulture);

    private static decimal? ParseMoneyOrNull(PayPalMoney? money) =>
        money?.Value is null ? null : decimal.Parse(money.Value, CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParsePayPalDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    private static string ToPayPalDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static GatewayAuthorizationInfo ToAuthorizationInfo(PayPalAuthorization auth) => new(
        auth.Id ?? string.Empty,
        auth.Status ?? string.Empty,
        ParseMoney(auth.Amount),
        auth.Amount?.CurrencyCode ?? string.Empty,
        ParsePayPalDate(auth.ExpirationTime));

    // ------------------------------------------------------------------
    // PayPal wire DTOs (responses and error shape)
    // ------------------------------------------------------------------

    private sealed class PayPalTokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    private sealed class PayPalMoney
    {
        [JsonPropertyName("currency_code")] public string? CurrencyCode { get; set; }
        [JsonPropertyName("value")] public string? Value { get; set; }
    }

    private sealed class PayPalOrder
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("purchase_units")] public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }
    }

    private sealed class PayPalPurchaseUnit
    {
        [JsonPropertyName("payments")] public PayPalPayments? Payments { get; set; }
    }

    private sealed class PayPalPayments
    {
        [JsonPropertyName("authorizations")] public List<PayPalAuthorization>? Authorizations { get; set; }
    }

    private sealed class PayPalAuthorization
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
        [JsonPropertyName("expiration_time")] public string? ExpirationTime { get; set; }
    }

    private sealed class PayPalCapture
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
        [JsonPropertyName("seller_receivable_breakdown")] public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
    }

    private sealed class PayPalSellerReceivableBreakdown
    {
        [JsonPropertyName("gross_amount")] public PayPalMoney? GrossAmount { get; set; }
        [JsonPropertyName("paypal_fee")] public PayPalMoney? PayPalFee { get; set; }
        [JsonPropertyName("net_amount")] public PayPalMoney? NetAmount { get; set; }
    }

    private sealed class PayPalRefund
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
    }

    private sealed class PayPalVaultToken
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("customer")] public PayPalVaultCustomer? Customer { get; set; }
        [JsonPropertyName("payment_source")] public PayPalVaultPaymentSource? PaymentSource { get; set; }
    }

    private sealed class PayPalVaultCustomer
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
    }

    private sealed class PayPalVaultPaymentSource
    {
        [JsonPropertyName("card")] public PayPalVaultCard? Card { get; set; }
    }

    private sealed class PayPalVaultCard
    {
        [JsonPropertyName("brand")] public string? Brand { get; set; }
        [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
        [JsonPropertyName("expiry")] public string? Expiry { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class PayPalTransactionSearchResponse
    {
        [JsonPropertyName("transaction_details")] public List<PayPalTransactionDetail>? TransactionDetails { get; set; }
        [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
    }

    private sealed class PayPalTransactionDetail
    {
        [JsonPropertyName("transaction_info")] public PayPalTransactionInfo? TransactionInfo { get; set; }
    }

    private sealed class PayPalTransactionInfo
    {
        [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
        [JsonPropertyName("transaction_event_code")] public string? TransactionEventCode { get; set; }
        [JsonPropertyName("transaction_status")] public string? TransactionStatus { get; set; }
        [JsonPropertyName("transaction_amount")] public PayPalMoney? TransactionAmount { get; set; }
        [JsonPropertyName("fee_amount")] public PayPalMoney? FeeAmount { get; set; }
        [JsonPropertyName("transaction_initiation_date")] public string? TransactionInitiationDate { get; set; }
        [JsonPropertyName("transaction_updated_date")] public string? TransactionUpdatedDate { get; set; }
    }

    private sealed class PayPalError
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("debug_id")] public string? DebugId { get; set; }
        [JsonPropertyName("details")] public List<PayPalErrorDetail>? Details { get; set; }
    }

    private sealed class PayPalErrorDetail
    {
        [JsonPropertyName("issue")] public string? Issue { get; set; }
    }
}

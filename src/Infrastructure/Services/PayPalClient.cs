using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Plain-HTTP PayPal REST client. Verified against the official PayPal docs:
/// Orders v2, Payments v2, Vault v3 (payment method tokens) and Transaction Search v1.
/// Card data passes through here to PayPal only; it is never persisted or logged.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalSettings> settings, ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    private string BaseUrl => _settings.ResolveBaseUrl();

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

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

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new PayPalApiException(response.StatusCode, null,
                    $"PayPal token request failed with status {(int)response.StatusCode}.", DebugIdOf(response));
            }

            var token = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions)
                ?? throw new PayPalApiException(response.StatusCode, null, "PayPal token response was empty.", DebugIdOf(response));

            _accessToken = token.AccessToken!;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn - 60);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? requestId, CancellationToken cancellationToken)
    {
        var accessToken = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, $"{BaseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (body != null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }
        else if (method == HttpMethod.Post)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            string? name = null;
            string? message = null;
            string? debugId = DebugIdOf(response);
            try
            {
                var error = JsonSerializer.Deserialize<ErrorResponse>(errorBody, JsonOptions);
                name = error?.Name;
                message = error?.Message;
                debugId ??= error?.DebugId;
            }
            catch (JsonException) { /* body was not a PayPal error document */ }

            _logger.LogWarning("PayPal {Method} {Path} failed: {Status} {Name} {Message} (debug id {DebugId})",
                method, path, (int)response.StatusCode, name, message, debugId);

            throw new PayPalApiException(response.StatusCode, name,
                $"PayPal {method} {path} failed ({(int)response.StatusCode} {name}): {message}", debugId);
        }

        return response;
    }

    private static string? DebugIdOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Paypal-Debug-Id", out var values) ? string.Join(",", values) : null;

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new InvalidOperationException("PayPal returned an empty response body.");
    }

    public async Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, PayPalCardDetails card, string referenceId, string requestId, CancellationToken cancellationToken = default)
    {
        var paymentSource = new
        {
            card = new
            {
                number = card.Number,
                expiry = card.Expiry,
                security_code = card.SecurityCode,
                name = card.Name,
                billing_address = card.BillingAddress == null ? null : new
                {
                    address_line_1 = card.BillingAddress.AddressLine1,
                    admin_area_2 = card.BillingAddress.City,
                    admin_area_1 = card.BillingAddress.State,
                    postal_code = card.BillingAddress.PostalCode,
                    country_code = card.BillingAddress.CountryCode
                }
            }
        };

        return await CreateAndAuthorizeOrderAsync(amount, currency, paymentSource, referenceId, requestId, cancellationToken);
    }

    public async Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultTokenId, string referenceId, string requestId, CancellationToken cancellationToken = default)
    {
        var paymentSource = new
        {
            card = new
            {
                vault_id = vaultTokenId,
                stored_credential = new
                {
                    payment_initiator = "MERCHANT",
                    payment_type = "UNSCHEDULED",
                    usage = "SUBSEQUENT"
                }
            }
        };

        return await CreateAndAuthorizeOrderAsync(amount, currency, paymentSource, referenceId, requestId, cancellationToken);
    }

    private async Task<PayPalAuthorizationResult> CreateAndAuthorizeOrderAsync(decimal amount, string currency, object paymentSource, string referenceId, string requestId, CancellationToken cancellationToken)
    {
        var createBody = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = referenceId,
                    custom_id = referenceId,
                    amount = new { currency_code = currency, value = FormatAmount(amount) }
                }
            },
            payment_source = paymentSource
        };

        using var createResponse = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", createBody, $"{requestId}-order", cancellationToken);
        var order = await ReadAsync<OrderResponse>(createResponse, cancellationToken);

        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalPayerActionRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (PAYER_ACTION_REQUIRED), which this integration does not support.");
        }

        // For direct card payments the create call already authorizes: the order comes
        // back COMPLETED with the authorization under purchase_units/payments. Only call
        // the authorize endpoint when the order is still awaiting it.
        var authorized = order;
        if (order.FirstAuthorization() == null)
        {
            using var authorizeResponse = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{order.Id}/authorize", null, $"{requestId}-authorize", cancellationToken);
            authorized = await ReadAsync<OrderResponse>(authorizeResponse, cancellationToken);

            if (string.Equals(authorized.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PayPalPayerActionRequiredException(
                    "PayPal requires the shopper to approve this card payment in a browser (PAYER_ACTION_REQUIRED), which this integration does not support.");
            }
        }

        var authorization = authorized.FirstAuthorization()
            ?? throw new PayPalApiException(HttpStatusCode.OK, null, "PayPal authorize response contained no authorization.", null);

        return new PayPalAuthorizationResult(
            authorized.Id!,
            authorization.Id!,
            authorization.Status ?? authorized.Status ?? "UNKNOWN",
            ParseAmount(authorization.Amount?.Value) ?? amount,
            authorization.Amount?.CurrencyCode ?? currency,
            ParsePayPalDate(authorization.ExpirationTime));
    }

    public async Task<PayPalAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        var authorization = await ReadAsync<AuthorizationResponse>(response, cancellationToken);
        return new PayPalAuthorizationState(authorization.Id!, authorization.Status ?? "UNKNOWN",
            ParseAmount(authorization.Amount?.Value), authorization.Amount?.CurrencyCode, ParsePayPalDate(authorization.ExpirationTime));
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = new { currency_code = currency, value = FormatAmount(amount) },
            final_capture = true
        };

        using var response = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, requestId, cancellationToken);
        var capture = await ReadAsync<CaptureResponse>(response, cancellationToken);

        return new PayPalCaptureResult(
            capture.Id!,
            capture.Status ?? "UNKNOWN",
            ParseAmount(capture.SellerReceivableBreakdown?.GrossAmount?.Value) ?? ParseAmount(capture.Amount?.Value) ?? amount,
            capture.SellerReceivableBreakdown?.GrossAmount?.CurrencyCode ?? currency,
            ParseAmount(capture.SellerReceivableBreakdown?.PayPalFee?.Value),
            ParseAmount(capture.SellerReceivableBreakdown?.NetAmount?.Value));
    }

    public async Task<PayPalAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new { amount = new { currency_code = currency, value = FormatAmount(amount) } };

        using var response = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, requestId, cancellationToken);
        var authorization = await ReadAsync<AuthorizationResponse>(response, cancellationToken);

        return new PayPalAuthorizationState(authorization.Id!, authorization.Status ?? "UNKNOWN",
            ParseAmount(authorization.Amount?.Value), authorization.Amount?.CurrencyCode, ParsePayPalDate(authorization.ExpirationTime));
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, requestId, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = new { currency_code = currency, value = FormatAmount(amount) },
            invoice_id = requestId,
            note_to_payer = "Refund for your eShopOnWeb order"
        };

        using var response = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, requestId, cancellationToken);
        var refund = await ReadAsync<RefundResponse>(response, cancellationToken);

        return new PayPalRefundResult(refund.Id!, refund.Status ?? "UNKNOWN",
            ParseAmount(refund.Amount?.Value) ?? amount, refund.Amount?.CurrencyCode ?? currency);
    }

    public async Task<PayPalSetupTokenResult> CreateSetupTokenAsync(PayPalCardDetails card, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            payment_source = new
            {
                card = new
                {
                    number = card.Number,
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    name = card.Name,
                    billing_address = card.BillingAddress == null ? null : new
                    {
                        address_line_1 = card.BillingAddress.AddressLine1,
                        admin_area_2 = card.BillingAddress.City,
                        admin_area_1 = card.BillingAddress.State,
                        postal_code = card.BillingAddress.PostalCode,
                        country_code = card.BillingAddress.CountryCode
                    }
                }
            }
        };

        using var response = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", body, requestId, cancellationToken);
        var setupToken = await ReadAsync<SetupTokenResponse>(response, cancellationToken);

        if (string.Equals(setupToken.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalPayerActionRequiredException(
                "PayPal requires the shopper to approve saving this card in a browser (PAYER_ACTION_REQUIRED), which this integration does not support.");
        }

        return new PayPalSetupTokenResult(setupToken.Id!, setupToken.Status ?? "UNKNOWN", setupToken.Customer?.Id);
    }

    public async Task<PayPalPaymentTokenResult> CreatePaymentTokenAsync(string setupTokenId, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            payment_source = new
            {
                token = new { id = setupTokenId, type = "SETUP_TOKEN" }
            }
        };

        using var response = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, requestId, cancellationToken);
        var token = await ReadAsync<PaymentTokenResponse>(response, cancellationToken);

        return new PayPalPaymentTokenResult(
            token.Id!,
            token.Customer?.Id,
            token.PaymentSource?.Card?.Brand,
            token.PaymentSource?.Card?.LastDigits,
            token.PaymentSource?.Card?.Expiry,
            token.PaymentSource?.Card?.Name);
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{paymentTokenId}", null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransactionInfo>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransactionInfo>();

        // The Transaction Search API supports a maximum range of 31 days per request.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31) < to ? windowStart.AddDays(31) : to;
            await SearchTransactionWindowAsync(windowStart, windowEnd, results, cancellationToken);
            windowStart = windowEnd;
        }

        return results;
    }

    private async Task SearchTransactionWindowAsync(DateTimeOffset from, DateTimeOffset to, List<PayPalTransactionInfo> results, CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        var page = 1;
        var totalPages = 1;

        while (page <= totalPages)
        {
            var path = $"/v1/reporting/transactions?start_date={FormatPayPalDate(from)}&end_date={FormatPayPalDate(to)}&fields=transaction_info&page_size={pageSize}&page={page}";
            using var response = await SendAsync(HttpMethod.Get, path, null, null, cancellationToken);
            var search = await ReadAsync<TransactionSearchResponse>(response, cancellationToken);

            totalPages = search.TotalPages ?? 1;
            if (search.TransactionDetails != null)
            {
                foreach (var detail in search.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId == null) continue;

                    results.Add(new PayPalTransactionInfo(
                        info.TransactionId,
                        info.PayPalReferenceId,
                        info.PayPalReferenceIdType,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        ParseAmount(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode,
                        ParseAmount(info.FeeAmount?.Value),
                        ParsePayPalDate(info.TransactionInitiationDate),
                        ParsePayPalDate(info.TransactionUpdatedDate)));
                }
            }

            page++;
        }
    }

    private static string FormatPayPalDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : null;

    private static DateTimeOffset? ParsePayPalDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;

    private class TokenResponse
    {
        public string? AccessToken { get; set; }
        public int ExpiresIn { get; set; }
    }

    private class ErrorResponse
    {
        public string? Name { get; set; }
        public string? Message { get; set; }
        public string? DebugId { get; set; }
    }

    private class MoneyResponse
    {
        public string? CurrencyCode { get; set; }
        public string? Value { get; set; }
    }

    private class OrderResponse
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public List<PurchaseUnitResponse>? PurchaseUnits { get; set; }

        public AuthorizationResponse? FirstAuthorization()
        {
            if (PurchaseUnits == null) return null;
            foreach (var unit in PurchaseUnits)
            {
                var authorizations = unit.Payments?.Authorizations;
                if (authorizations != null && authorizations.Count > 0)
                {
                    return authorizations[0];
                }
            }
            return null;
        }
    }

    private class PurchaseUnitResponse
    {
        public PaymentsResponse? Payments { get; set; }
    }

    private class PaymentsResponse
    {
        public List<AuthorizationResponse>? Authorizations { get; set; }
    }

    private class AuthorizationResponse
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public MoneyResponse? Amount { get; set; }
        public string? ExpirationTime { get; set; }
    }

    private class CaptureResponse
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public MoneyResponse? Amount { get; set; }
        public SellerReceivableBreakdownResponse? SellerReceivableBreakdown { get; set; }
    }

    private class SellerReceivableBreakdownResponse
    {
        public MoneyResponse? GrossAmount { get; set; }
        [JsonPropertyName("paypal_fee")]
        public MoneyResponse? PayPalFee { get; set; }
        public MoneyResponse? NetAmount { get; set; }
    }

    private class RefundResponse
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public MoneyResponse? Amount { get; set; }
    }

    private class SetupTokenResponse
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public CustomerResponse? Customer { get; set; }
    }

    private class CustomerResponse
    {
        public string? Id { get; set; }
    }

    private class PaymentTokenResponse
    {
        public string? Id { get; set; }
        public CustomerResponse? Customer { get; set; }
        public PaymentTokenSourceResponse? PaymentSource { get; set; }
    }

    private class PaymentTokenSourceResponse
    {
        public VaultedCardResponse? Card { get; set; }
    }

    private class VaultedCardResponse
    {
        public string? Brand { get; set; }
        public string? LastDigits { get; set; }
        public string? Expiry { get; set; }
        public string? Name { get; set; }
    }

    private class TransactionSearchResponse
    {
        public List<TransactionDetailResponse>? TransactionDetails { get; set; }
        public int? TotalPages { get; set; }
        public int? Page { get; set; }
    }

    private class TransactionDetailResponse
    {
        public TransactionInfoResponse? TransactionInfo { get; set; }
    }

    private class TransactionInfoResponse
    {
        public string? TransactionId { get; set; }
        [JsonPropertyName("paypal_reference_id")]
        public string? PayPalReferenceId { get; set; }
        [JsonPropertyName("paypal_reference_id_type")]
        public string? PayPalReferenceIdType { get; set; }
        public string? TransactionEventCode { get; set; }
        public string? TransactionStatus { get; set; }
        public MoneyResponse? TransactionAmount { get; set; }
        public MoneyResponse? FeeAmount { get; set; }
        public string? TransactionInitiationDate { get; set; }
        public string? TransactionUpdatedDate { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal REST client covering Orders v2, Payments v2, Vault v3 and
/// Transaction Search v1. Full card details pass through here in transit
/// only; request bodies are never logged.
/// </summary>
public class PayPalApiClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const int TransactionSearchMaxRangeDays = 31;
    private const int TransactionSearchPageSize = 100;

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalApiClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalApiClient(HttpClient httpClient, PayPalSettings settings, ILogger<PayPalApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(settings.ApiBaseUrl + "/");
    }

    public async Task<string> CreateOrderAsync(decimal amount, string currency, string referenceId, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new CreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    ReferenceId = referenceId,
                    CustomId = referenceId,
                    InvoiceId = invoiceId,
                    Amount = new MoneyRequest { CurrencyCode = currency, Value = FormatMoney(amount) }
                }
            }
        };

        var response = await SendAsync<CreateOrderRequest, OrderResponse>(
            HttpMethod.Post, "v2/checkout/orders", request, idempotencyKey, cancellationToken);

        if (response.Id is null)
        {
            throw new PayPalApiException(200, null, null, null, "PayPal did not return an order id.");
        }
        return response.Id;
    }

    public Task<PayPalAuthorizationInfo> AuthorizeOrderWithCardAsync(string payPalOrderId, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new AuthorizeOrderRequest
        {
            PaymentSource = new PaymentSourceRequest
            {
                Card = new CardRequest
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.Name,
                    BillingAddress = MapAddress(card.Address)
                }
            }
        };
        return AuthorizeOrderAsync(payPalOrderId, request, idempotencyKey, cancellationToken);
    }

    public Task<PayPalAuthorizationInfo> AuthorizeOrderWithVaultedCardAsync(string payPalOrderId, string vaultTokenId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new AuthorizeOrderRequest
        {
            PaymentSource = new PaymentSourceRequest
            {
                Card = new CardRequest
                {
                    VaultId = vaultTokenId,
                    StoredCredential = new StoredCredentialRequest
                    {
                        PaymentInitiator = "CUSTOMER",
                        PaymentType = "ONE_TIME",
                        Usage = "SUBSEQUENT"
                    }
                }
            }
        };
        return AuthorizeOrderAsync(payPalOrderId, request, idempotencyKey, cancellationToken);
    }

    public async Task<PayPalAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<AuthorizationResponse>(
            HttpMethod.Get, $"v2/payments/authorizations/{authorizationId}", null, cancellationToken);
        return MapAuthorization(response);
    }

    public async Task<PayPalAuthorizationInfo> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new ReauthorizeRequest
        {
            Amount = new MoneyRequest { CurrencyCode = currency, Value = FormatMoney(amount) }
        };
        var response = await SendAsync<ReauthorizeRequest, AuthorizationResponse>(
            HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/reauthorize", request, idempotencyKey, cancellationToken);
        return MapAuthorization(response);
    }

    public async Task<PayPalCaptureInfo> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // No invoice_id on the capture: PayPal then reports the authorizing
        // transaction's invoice id, keeping reconciliation matching stable.
        var request = new CaptureRequest
        {
            Amount = new MoneyRequest { CurrencyCode = currency, Value = FormatMoney(amount) },
            FinalCapture = true
        };
        var response = await SendAsync<CaptureRequest, CaptureResponse>(
            HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/capture", request, idempotencyKey, cancellationToken);

        return new PayPalCaptureInfo(
            response.Id ?? string.Empty,
            response.Status ?? string.Empty,
            ParseMoney(response.SellerReceivableBreakdown?.GrossAmount) ?? ParseMoney(response.Amount) ?? 0m,
            ParseMoney(response.SellerReceivableBreakdown?.PayPalFee),
            ParseMoney(response.SellerReceivableBreakdown?.NetAmount),
            response.Amount?.CurrencyCode ?? currency);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await SendAsync<object, AuthorizationResponse>(
            HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void", new { }, idempotencyKey, cancellationToken);
    }

    public async Task<PayPalRefundInfo> RefundCaptureAsync(string captureId, decimal? amount, string currency, string? noteToPayer, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new RefundRequest
        {
            Amount = amount is null ? null : new MoneyRequest { CurrencyCode = currency, Value = FormatMoney(amount.Value) },
            NoteToPayer = noteToPayer
        };
        var response = await SendAsync<RefundRequest, RefundResponse>(
            HttpMethod.Post, $"v2/payments/captures/{captureId}/refund", request, idempotencyKey, cancellationToken);

        return new PayPalRefundInfo(
            response.Id ?? string.Empty,
            response.Status ?? string.Empty,
            ParseMoney(response.Amount) ?? amount ?? 0m,
            response.Amount?.CurrencyCode ?? currency);
    }

    public async Task<PayPalCardTokenInfo> CreatePaymentTokenAsync(CardDetails card, string merchantCustomerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new CreatePaymentTokenRequest
        {
            PaymentSource = new PaymentTokenSourceRequest
            {
                Card = new CardRequest
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.Name,
                    BillingAddress = MapAddress(card.Address)
                }
            },
            Customer = new CustomerRequest { MerchantCustomerId = merchantCustomerId }
        };

        var response = await SendAsync<CreatePaymentTokenRequest, PaymentTokenResponse>(
            HttpMethod.Post, "v3/vault/payment-tokens", request, idempotencyKey, cancellationToken);

        if (response.Id is null)
        {
            throw new PayPalApiException(200, null, null, null, "PayPal did not return a payment token id.");
        }

        return new PayPalCardTokenInfo(
            response.Id,
            response.PaymentSource?.Card?.Brand,
            response.PaymentSource?.Card?.LastDigits,
            response.PaymentSource?.Card?.Expiry,
            response.PaymentSource?.Card?.Name);
    }

    public async Task DeletePaymentTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"v3/vault/payment-tokens/{vaultTokenId}", null, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransactionInfo>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransactionInfo>();

        // The API supports a maximum window of 31 days per request: chunk longer ranges.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(TransactionSearchMaxRangeDays) < to
                ? windowStart.AddDays(TransactionSearchMaxRangeDays)
                : to;

            var page = 1;
            while (true)
            {
                var path = "v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(FormatTimestamp(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatTimestamp(windowEnd))}" +
                    $"&fields=all&page_size={TransactionSearchPageSize}&page={page}";

                var response = await SendAsync<TransactionSearchResponse>(HttpMethod.Get, path, null, cancellationToken);

                if (response.TransactionDetails is not null)
                {
                    results.AddRange(response.TransactionDetails
                        .Where(d => d.TransactionInfo?.TransactionId is not null)
                        .Select(d => new PayPalTransactionInfo(
                            d.TransactionInfo!.TransactionId!,
                            d.TransactionInfo.PayPalReferenceId,
                            d.TransactionInfo.PayPalReferenceIdType,
                            d.TransactionInfo.TransactionEventCode,
                            d.TransactionInfo.TransactionStatus,
                            ParseMoney(d.TransactionInfo.TransactionAmount),
                            d.TransactionInfo.TransactionAmount?.CurrencyCode,
                            ParseMoney(d.TransactionInfo.FeeAmount),
                            d.TransactionInfo.InvoiceId,
                            d.TransactionInfo.CustomField,
                            d.TransactionInfo.TransactionInitiationDate,
                            d.TransactionInfo.TransactionUpdatedDate)));
                }

                var totalPages = response.TotalPages ?? 1;
                if (page >= totalPages || response.TransactionDetails is null || response.TransactionDetails.Count == 0)
                {
                    break;
                }
                page++;
            }

            windowStart = windowEnd;
        }

        return results;
    }

    private async Task<PayPalAuthorizationInfo> AuthorizeOrderAsync(string payPalOrderId, AuthorizeOrderRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        var response = await SendAsync<AuthorizeOrderRequest, OrderResponse>(
            HttpMethod.Post, $"v2/checkout/orders/{payPalOrderId}/authorize", request, idempotencyKey, cancellationToken);

        if (response.Links?.Any(l => l.Rel == "payer-action") == true
            || string.Equals(response.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentRequiresBuyerActionException(
                "PayPal answered the card payment with a challenge that requires the shopper to approve in a browser. " +
                "This integration does not support approval round-trips.");
        }

        var authorization = response.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<AuthorizationResponse>())
            .FirstOrDefault();

        if (authorization?.Id is null)
        {
            throw new PayPalApiException(200, null, null, null,
                $"PayPal did not return an authorization for order {payPalOrderId} (order status {response.Status}).");
        }

        return MapAuthorization(authorization);
    }

    private static PayPalAuthorizationInfo MapAuthorization(AuthorizationResponse response)
    {
        return new PayPalAuthorizationInfo(
            response.Id ?? string.Empty,
            response.Status ?? string.Empty,
            ParseMoney(response.Amount) ?? 0m,
            response.Amount?.CurrencyCode ?? string.Empty,
            response.ExpirationTime);
    }

    private static AddressRequest? MapAddress(BillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }
        return new AddressRequest
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static string FormatMoney(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(MoneyRequest? money) =>
        money?.Value is null ? null : decimal.Parse(money.Value, CultureInfo.InvariantCulture);

    private async Task<TResponse> SendAsync<TRequest, TResponse>(HttpMethod method, string path, TRequest? body, string? idempotencyKey, CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class
    {
        using var request = await BuildRequestAsync(method, path, idempotencyKey, cancellationToken);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }
        return await SendCoreAsync<TResponse>(request, cancellationToken);
    }

    private async Task<TResponse> SendAsync<TResponse>(HttpMethod method, string path, string? idempotencyKey, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = await BuildRequestAsync(method, path, idempotencyKey, cancellationToken);
        return await SendCoreAsync<TResponse>(request, cancellationToken);
    }

    private async Task SendAsync(HttpMethod method, string path, string? idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = await BuildRequestAsync(method, path, idempotencyKey, cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, cancellationToken);
        }
    }

    private async Task<HttpRequestMessage> BuildRequestAsync(HttpMethod method, string path, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        return request;
    }

    private async Task<TResponse> SendCoreAsync<TResponse>(HttpRequestMessage request, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, cancellationToken);
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null!;
        }
        return JsonSerializer.Deserialize<TResponse>(content, JsonOptions)!;
    }

    private static async Task ThrowApiExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        string? name = null, message = null, debugId = null, issue = null;
        try
        {
            var error = JsonSerializer.Deserialize<PayPalErrorResponse>(content, JsonOptions);
            name = error?.Name;
            message = error?.Message;
            debugId = error?.DebugId;
            issue = error?.Details?.FirstOrDefault()?.Issue;
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with status only.
        }

        throw new PayPalApiException(
            (int)response.StatusCode, name, issue, debugId,
            $"PayPal request failed with status {(int)response.StatusCode} ({name ?? "unknown"}: {issue ?? message ?? "no details"}, debug_id {debugId ?? "n/a"}).");
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

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowApiExceptionAsync(response, cancellationToken);
            }

            var token = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(JsonOptions, cancellationToken);
            if (token?.AccessToken is null)
            {
                throw new PayPalApiException((int)response.StatusCode, null, null, null, "PayPal did not return an access token.");
            }

            _accessToken = token.AccessToken;
            // Refresh a minute early to avoid using a token at the very edge of expiry.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn - 60);
            _logger.LogInformation("Obtained PayPal access token, valid for {ExpiresIn}s.", token.ExpiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    #region PayPal wire DTOs

    private class OAuthTokenResponse
    {
        public string? AccessToken { get; set; }
        public int ExpiresIn { get; set; }
    }

    private class PayPalErrorResponse
    {
        public string? Name { get; set; }
        public string? Message { get; set; }
        public string? DebugId { get; set; }
        public List<PayPalErrorDetail>? Details { get; set; }
    }

    private class PayPalErrorDetail
    {
        public string? Issue { get; set; }
    }

    private class MoneyRequest
    {
        public string? CurrencyCode { get; set; }
        public string? Value { get; set; }
    }

    private class CreateOrderRequest
    {
        public string Intent { get; set; } = "AUTHORIZE";
        public List<PurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    }

    private class PurchaseUnitRequest
    {
        public string? ReferenceId { get; set; }
        public string? CustomId { get; set; }
        public string? InvoiceId { get; set; }
        public MoneyRequest? Amount { get; set; }
    }

    private class AuthorizeOrderRequest
    {
        public PaymentSourceRequest? PaymentSource { get; set; }
    }

    private class PaymentSourceRequest
    {
        public CardRequest? Card { get; set; }
    }

    private class CardRequest
    {
        public string? Number { get; set; }
        public string? Expiry { get; set; }
        public string? SecurityCode { get; set; }
        public string? Name { get; set; }
        public AddressRequest? BillingAddress { get; set; }
        public string? VaultId { get; set; }
        public StoredCredentialRequest? StoredCredential { get; set; }
    }

    private class StoredCredentialRequest
    {
        public string? PaymentInitiator { get; set; }
        public string? PaymentType { get; set; }
        public string? Usage { get; set; }
    }

    private class AddressRequest
    {
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? AdminArea2 { get; set; }
        public string? AdminArea1 { get; set; }
        public string? PostalCode { get; set; }
        public string? CountryCode { get; set; }
    }

    private class OrderResponse
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public List<PurchaseUnitResponse>? PurchaseUnits { get; set; }
        public List<LinkResponse>? Links { get; set; }
    }

    private class PurchaseUnitResponse
    {
        public PaymentCollectionResponse? Payments { get; set; }
    }

    private class PaymentCollectionResponse
    {
        public List<AuthorizationResponse>? Authorizations { get; set; }
    }

    private class LinkResponse
    {
        public string? Rel { get; set; }
        public string? Href { get; set; }
    }

    private class AuthorizationResponse
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public MoneyRequest? Amount { get; set; }
        public DateTimeOffset? ExpirationTime { get; set; }
    }

    private class ReauthorizeRequest
    {
        public MoneyRequest? Amount { get; set; }
    }

    private class CaptureRequest
    {
        public MoneyRequest? Amount { get; set; }
        public string? InvoiceId { get; set; }
        public bool FinalCapture { get; set; }
    }

    private class CaptureResponse
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public MoneyRequest? Amount { get; set; }
        public SellerReceivableBreakdownResponse? SellerReceivableBreakdown { get; set; }
    }

    private class SellerReceivableBreakdownResponse
    {
        public MoneyRequest? GrossAmount { get; set; }
        [JsonPropertyName("paypal_fee")]
        public MoneyRequest? PayPalFee { get; set; }
        public MoneyRequest? NetAmount { get; set; }
    }

    private class RefundRequest
    {
        public MoneyRequest? Amount { get; set; }
        public string? NoteToPayer { get; set; }
    }

    private class RefundResponse
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public MoneyRequest? Amount { get; set; }
    }

    private class CreatePaymentTokenRequest
    {
        public PaymentTokenSourceRequest? PaymentSource { get; set; }
        public CustomerRequest? Customer { get; set; }
    }

    private class PaymentTokenSourceRequest
    {
        public CardRequest? Card { get; set; }
    }

    private class CustomerRequest
    {
        public string? MerchantCustomerId { get; set; }
    }

    private class PaymentTokenResponse
    {
        public string? Id { get; set; }
        public PaymentTokenSourceResponse? PaymentSource { get; set; }
    }

    private class PaymentTokenSourceResponse
    {
        public CardTokenResponse? Card { get; set; }
    }

    private class CardTokenResponse
    {
        public string? Brand { get; set; }
        public string? LastDigits { get; set; }
        public string? Expiry { get; set; }
        public string? Name { get; set; }
    }

    private class TransactionSearchResponse
    {
        public int? TotalPages { get; set; }
        public int? Page { get; set; }
        public List<TransactionDetailResponse>? TransactionDetails { get; set; }
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
        public MoneyRequest? TransactionAmount { get; set; }
        public MoneyRequest? FeeAmount { get; set; }
        public string? InvoiceId { get; set; }
        public string? CustomField { get; set; }
        public DateTimeOffset? TransactionInitiationDate { get; set; }
        public DateTimeOffset? TransactionUpdatedDate { get; set; }
    }

    #endregion
}

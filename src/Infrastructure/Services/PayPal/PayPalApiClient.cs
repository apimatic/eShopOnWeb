using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// PayPal Payments API client: Orders v2 (authorize), Payments v2 (capture/reauthorize/void/refund),
/// Vault v3 (payment tokens) and Transaction Search v1 (reporting).
/// Full card data passes through here to PayPal only; it is never logged or persisted.
/// </summary>
public class PayPalApiClient : IPayPalClient
{
    private const int TransactionSearchMaxWindowDays = 31;
    private const int TransactionSearchPageSize = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalApiClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalApiClient(HttpClient httpClient, PayPalSettings settings, ILogger<PayPalApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(settings.ApiBaseUrl + "/");
        }
    }

    public async Task<string> CreateOrderAsync(decimal amount, string currency, string referenceId, string invoiceId,
        string requestId, CancellationToken cancellationToken = default)
    {
        // The payment source is supplied on the authorize call, not here: a single-step create
        // with a card is refused, and with a vault token it authorizes immediately.
        var body = new CreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    ReferenceId = referenceId,
                    CustomId = invoiceId,
                    InvoiceId = invoiceId,
                    Amount = new MoneyDto { CurrencyCode = currency, Value = FormatAmount(amount) }
                }
            }
        };

        var response = await SendAsync<CreateOrderRequest, OrderResponse>(HttpMethod.Post,
            "v2/checkout/orders", body, requestId, false, cancellationToken);

        if (string.Equals(response.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                "PayPal requires the shopper to approve this payment through a browser challenge " +
                "(e.g. 3D Secure), which this server-to-server integration does not support.");
        }

        return response.Id ?? throw new PayPalApiException(HttpStatusCode.InternalServerError,
            null, null, "PayPal create-order response did not contain an order id.", null);
    }

    public async Task<PayPalAuthorization> AuthorizeOrderAsync(string payPalOrderId,
        PayPalCard? card, string? vaultTokenId, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new AuthorizeOrderRequest { PaymentSource = BuildPaymentSource(card, vaultTokenId) };

        var response = await SendAsync<AuthorizeOrderRequest, OrderResponse>(HttpMethod.Post,
            $"v2/checkout/orders/{payPalOrderId}/authorize", body, requestId, true, cancellationToken);

        if (string.Equals(response.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                "PayPal requires the shopper to approve this payment through a browser challenge " +
                "(e.g. 3D Secure), which this server-to-server integration does not support.");
        }

        var authorization = response.PurchaseUnits?
            .SelectMany(pu => pu.Payments?.Authorizations ?? new List<AuthorizationDto>())
            .FirstOrDefault();

        if (authorization?.Id is null)
        {
            throw new PayPalApiException(HttpStatusCode.InternalServerError, null, null,
                $"PayPal authorize response for order {payPalOrderId} did not contain an authorization.", null);
        }

        if (authorization.ExpirationTime is null)
        {
            var details = await GetAuthorizationAsync(authorization.Id, cancellationToken);
            return details with { Status = authorization.Status ?? details.Status };
        }

        return MapAuthorization(authorization);
    }

    public async Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<AuthorizationDto>(HttpMethod.Get,
            $"v2/payments/authorizations/{authorizationId}", null, cancellationToken);
        return MapAuthorization(response);
    }

    public async Task<PayPalCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string invoiceId, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new CaptureRequest
        {
            Amount = new MoneyDto { CurrencyCode = currency, Value = FormatAmount(amount) },
            InvoiceId = invoiceId,
            FinalCapture = true
        };

        // return=representation: the minimal response omits seller_receivable_breakdown (fee/net).
        var response = await SendAsync<CaptureRequest, CaptureDto>(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture", body, requestId,
            preferRepresentation: true, cancellationToken);

        return new PayPalCapture(
            response.Id ?? string.Empty,
            response.Status ?? string.Empty,
            ParseAmount(response.Amount?.Value) ?? amount,
            ParseAmount(response.SellerReceivableBreakdown?.PaypalFee?.Value),
            ParseAmount(response.SellerReceivableBreakdown?.NetAmount?.Value),
            response.Amount?.CurrencyCode ?? currency);
    }

    public async Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new MoneyDto { CurrencyCode = currency, Value = FormatAmount(amount) }
        };

        var response = await SendAsync<ReauthorizeRequest, AuthorizationDto>(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize", body, requestId, false, cancellationToken);
        return MapAuthorization(response);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken = default)
    {
        await SendAsync<object, object>(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/void", new { }, requestId, false, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string? noteToPayer, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new RefundRequest
        {
            Amount = amount is null ? null : new MoneyDto { CurrencyCode = currency, Value = FormatAmount(amount.Value) },
            NoteToPayer = noteToPayer
        };

        var response = await SendAsync<RefundRequest, RefundDto>(HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund", body, requestId, true, cancellationToken);

        return new PayPalRefundResult(
            response.Id ?? string.Empty,
            response.Status ?? string.Empty,
            ParseAmount(response.Amount?.Value) ?? amount ?? 0m,
            response.Amount?.CurrencyCode ?? currency);
    }

    public async Task<PayPalVaultToken> CreateCardPaymentTokenAsync(PayPalCard card, string customerId,
        string requestId, CancellationToken cancellationToken = default)
    {
        var body = new CreatePaymentTokenRequest
        {
            PaymentSource = new VaultPaymentSourceRequest { Card = MapCard(card) },
            // customer.id must match ^[0-9a-zA-Z_-]+$ (PayPal-generated style); our buyer ids are
            // emails, so we only set merchant_customer_id, which accepts them.
            Customer = new VaultCustomerRequest { MerchantCustomerId = customerId }
        };

        var response = await SendAsync<CreatePaymentTokenRequest, PaymentTokenResponse>(HttpMethod.Post,
            "v3/vault/payment-tokens", body, requestId, true, cancellationToken);

        if (response.Id is null)
        {
            throw new PayPalApiException(HttpStatusCode.InternalServerError, null, null,
                "PayPal vault response did not contain a payment token id.", null);
        }

        var cardResponse = response.PaymentSource?.Card;
        return new PayPalVaultToken(response.Id, cardResponse?.Brand, cardResponse?.LastDigits, cardResponse?.Expiry);
    }

    public async Task DeletePaymentTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(HttpMethod.Delete, $"v3/vault/payment-tokens/{vaultTokenId}", null, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(TransactionSearchMaxWindowDays);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            var page = 1;
            while (true)
            {
                var query = $"v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(FormatTimestamp(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatTimestamp(windowEnd))}" +
                    $"&fields=transaction_info" +
                    $"&page={page}&page_size={TransactionSearchPageSize}";

                var response = await SendAsync<TransactionSearchResponse>(HttpMethod.Get, query, null, cancellationToken);

                var details = response.TransactionDetails ?? new List<TransactionDetailDto>();
                foreach (var detail in details)
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null)
                    {
                        continue;
                    }

                    var key = $"{info.TransactionId}|{info.TransactionEventCode}|{info.TransactionInitiationDate}";
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    results.Add(new PayPalTransaction(
                        info.TransactionId,
                        info.PaypalReferenceId,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        ParseAmount(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode,
                        ParseAmount(info.FeeAmount?.Value),
                        ParseTimestamp(info.TransactionInitiationDate),
                        info.InvoiceId,
                        info.CustomField));
                }

                if (response.TotalPages is int totalPages)
                {
                    if (page >= totalPages)
                    {
                        break;
                    }
                }
                else if (details.Count < TransactionSearchPageSize)
                {
                    break;
                }

                page++;
            }

            windowStart = windowEnd;
        }

        return results;
    }

    private static PaymentSourceRequest? BuildPaymentSource(PayPalCard? card, string? vaultTokenId)
    {
        if (card is not null)
        {
            return new PaymentSourceRequest { Card = MapCard(card) };
        }

        if (!string.IsNullOrEmpty(vaultTokenId))
        {
            return new PaymentSourceRequest
            {
                Card = new CardRequest
                {
                    VaultId = vaultTokenId,
                    StoredCredential = new StoredCredentialRequest
                    {
                        PaymentInitiator = "CUSTOMER",
                        PaymentType = "ONE_TIME"
                    }
                }
            };
        }

        return null;
    }

    private static CardRequest MapCard(PayPalCard card)
    {
        return new CardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress is null ? null : new AddressDto
            {
                AddressLine1 = card.BillingAddress.AddressLine1,
                AddressLine2 = card.BillingAddress.AddressLine2,
                AdminArea2 = card.BillingAddress.AdminArea2,
                AdminArea1 = card.BillingAddress.AdminArea1,
                PostalCode = card.BillingAddress.PostalCode,
                CountryCode = card.BillingAddress.CountryCode
            }
        };
    }

    private static PayPalAuthorization MapAuthorization(AuthorizationDto dto)
    {
        return new PayPalAuthorization(
            dto.Id ?? string.Empty,
            dto.Status ?? string.Empty,
            ParseAmount(dto.Amount?.Value) ?? 0m,
            dto.Amount?.CurrencyCode ?? string.Empty,
            ParseTimestamp(dto.ExpirationTime));
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
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PayPal token request failed with status {StatusCode}", (int)response.StatusCode);
                throw new PayPalApiException(response.StatusCode, null, null,
                    "PayPal rejected the client credentials; token request failed.", null);
            }

            var token = JsonSerializer.Deserialize<TokenResponse>(payload, JsonOptions);
            _accessToken = token?.AccessToken
                ?? throw new PayPalApiException(response.StatusCode, null, null,
                    "PayPal token response did not contain an access token.", null);
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds((token!.ExpiresIn) - 60);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private Task<TResponse> SendAsync<TResponse>(HttpMethod method, string path, string? requestId,
        CancellationToken cancellationToken)
        where TResponse : class
        => SendAsync<object, TResponse>(method, path, null, requestId, false, cancellationToken);

    private async Task<TResponse> SendAsync<TRequest, TResponse>(HttpMethod method, string path, TRequest? body,
        string? requestId, bool preferRepresentation, CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class
    {
        var accessToken = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ToPayPalException(response.StatusCode, payload);
        }

        if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(payload))
        {
            return new object() as TResponse
                ?? throw new PayPalApiException(response.StatusCode, null, null,
                    "Unexpected empty PayPal response.", null);
        }

        return JsonSerializer.Deserialize<TResponse>(payload, JsonOptions)
            ?? throw new PayPalApiException(response.StatusCode, null, null,
                "Could not parse PayPal response.", null);
    }

    private PayPalApiException ToPayPalException(HttpStatusCode statusCode, string payload)
    {
        string? name = null, message = null, debugId = null, issue = null;
        try
        {
            var error = JsonSerializer.Deserialize<ErrorResponse>(payload, JsonOptions);
            name = error?.Name;
            message = error?.Message;
            debugId = error?.DebugId;
            issue = error?.Details?.FirstOrDefault()?.Issue;
        }
        catch (JsonException)
        {
            // payload was not a PayPal error document; keep generic message
        }

        _logger.LogWarning("PayPal API error {StatusCode} {Name}/{Issue} (debug id {DebugId})",
            (int)statusCode, name ?? "-", issue ?? "-", debugId ?? "-");

        return new PayPalApiException(statusCode, name, issue,
            message ?? $"PayPal API request failed with status {(int)statusCode}.", debugId);
    }

    private static string FormatAmount(decimal amount)
        => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? value)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateTimeOffset? ParseTimestamp(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;

    #region PayPal wire DTOs (snake_case via naming policy)

    private sealed class TokenResponse
    {
        public string? AccessToken { get; set; }
        public int ExpiresIn { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Name { get; set; }
        public string? Message { get; set; }
        public string? DebugId { get; set; }
        public List<ErrorDetailDto>? Details { get; set; }
    }

    private sealed class ErrorDetailDto
    {
        public string? Issue { get; set; }
    }

    private sealed class MoneyDto
    {
        public string? CurrencyCode { get; set; }
        public string? Value { get; set; }
    }

    private sealed class AddressDto
    {
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? AdminArea2 { get; set; }
        public string? AdminArea1 { get; set; }
        public string? PostalCode { get; set; }
        public string? CountryCode { get; set; }
    }

    private sealed class StoredCredentialRequest
    {
        public string? PaymentInitiator { get; set; }
        public string? PaymentType { get; set; }
    }

    private sealed class CardRequest
    {
        public string? Number { get; set; }
        public string? Expiry { get; set; }
        public string? SecurityCode { get; set; }
        public string? Name { get; set; }
        public AddressDto? BillingAddress { get; set; }
        public string? VaultId { get; set; }
        public StoredCredentialRequest? StoredCredential { get; set; }
    }

    private sealed class PaymentSourceRequest
    {
        public CardRequest? Card { get; set; }
    }

    private sealed class PurchaseUnitRequest
    {
        public string? ReferenceId { get; set; }
        public string? CustomId { get; set; }
        public string? InvoiceId { get; set; }
        public MoneyDto? Amount { get; set; }
    }

    private sealed class CreateOrderRequest
    {
        public string? Intent { get; set; }
        public List<PurchaseUnitRequest>? PurchaseUnits { get; set; }
        public PaymentSourceRequest? PaymentSource { get; set; }
    }

    private sealed class AuthorizeOrderRequest
    {
        public PaymentSourceRequest? PaymentSource { get; set; }
    }

    private sealed class AuthorizationDto
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public MoneyDto? Amount { get; set; }
        public string? ExpirationTime { get; set; }
    }

    private sealed class PaymentCollectionDto
    {
        public List<AuthorizationDto>? Authorizations { get; set; }
    }

    private sealed class PurchaseUnitResponse
    {
        public PaymentCollectionDto? Payments { get; set; }
    }

    private sealed class OrderResponse
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public List<PurchaseUnitResponse>? PurchaseUnits { get; set; }
    }

    private sealed class CaptureRequest
    {
        public MoneyDto? Amount { get; set; }
        public string? InvoiceId { get; set; }
        public bool FinalCapture { get; set; }
    }

    private sealed class SellerReceivableBreakdownDto
    {
        public MoneyDto? GrossAmount { get; set; }
        public MoneyDto? PaypalFee { get; set; }
        public MoneyDto? NetAmount { get; set; }
    }

    private sealed class CaptureDto
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public MoneyDto? Amount { get; set; }
        public SellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }
    }

    private sealed class ReauthorizeRequest
    {
        public MoneyDto? Amount { get; set; }
    }

    private sealed class RefundRequest
    {
        public MoneyDto? Amount { get; set; }
        public string? NoteToPayer { get; set; }
    }

    private sealed class RefundDto
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public MoneyDto? Amount { get; set; }
    }

    private sealed class VaultPaymentSourceRequest
    {
        public CardRequest? Card { get; set; }
    }

    private sealed class VaultCustomerRequest
    {
        public string? Id { get; set; }
        public string? MerchantCustomerId { get; set; }
    }

    private sealed class CreatePaymentTokenRequest
    {
        public VaultPaymentSourceRequest? PaymentSource { get; set; }
        public VaultCustomerRequest? Customer { get; set; }
    }

    private sealed class CardResponseDto
    {
        public string? Brand { get; set; }
        public string? LastDigits { get; set; }
        public string? Expiry { get; set; }
    }

    private sealed class VaultPaymentSourceResponse
    {
        public CardResponseDto? Card { get; set; }
    }

    private sealed class PaymentTokenResponse
    {
        public string? Id { get; set; }
        public VaultPaymentSourceResponse? PaymentSource { get; set; }
    }

    private sealed class TransactionInfoDto
    {
        public string? TransactionId { get; set; }
        public string? PaypalReferenceId { get; set; }
        public string? TransactionEventCode { get; set; }
        public string? TransactionStatus { get; set; }
        public MoneyDto? TransactionAmount { get; set; }
        public MoneyDto? FeeAmount { get; set; }
        public string? TransactionInitiationDate { get; set; }
        public string? InvoiceId { get; set; }
        public string? CustomField { get; set; }
    }

    private sealed class TransactionDetailDto
    {
        public TransactionInfoDto? TransactionInfo { get; set; }
    }

    private sealed class TransactionSearchResponse
    {
        public List<TransactionDetailDto>? TransactionDetails { get; set; }
        public int? TotalItems { get; set; }
        public int? TotalPages { get; set; }
        public int? Page { get; set; }
    }

    #endregion
}

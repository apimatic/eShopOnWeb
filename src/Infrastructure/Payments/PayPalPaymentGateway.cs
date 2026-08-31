using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/> over the PayPal REST APIs:
/// Orders v2 (create/authorize), Payments v2 (capture/reauthorize/void/refund),
/// Payment Method Tokens v3 (vault) and Transaction Search v1 (reporting).
/// OAuth tokens are cached until shortly before expiry. Card data passes through in
/// memory only and is never logged.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalPaymentGateway(HttpClient httpClient, IOptions<PayPalSettings> settings, ILogger<PayPalPaymentGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_settings.ResolveBaseUrl());
    }

    public string Currency => _settings.Currency;

    public async Task<PayPalOrderCreated> CreateOrderAsync(decimal amount, string currency, string referenceId, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
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
                    Amount = new MoneyDto { CurrencyCode = currency, Value = FormatAmount(amount) }
                }
            }
        };

        var response = await SendAsync<CreateOrderRequest, OrderResponse>(
            HttpMethod.Post, "/v2/checkout/orders", request, idempotencyKey, cancellationToken);

        _logger.LogInformation("PayPal order {PayPalOrderId} created for {ReferenceId} with status {Status}",
            response.Id, referenceId, response.Status);

        return new PayPalOrderCreated { Id = response.Id ?? string.Empty, Status = response.Status ?? string.Empty };
    }

    public async Task<PayPalAuthorizationInfo> AuthorizeOrderAsync(string payPalOrderId, GatewayPaymentSource paymentSource, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new AuthorizeOrderRequest { PaymentSource = BuildPaymentSource(paymentSource) };

        var response = await SendAsync<AuthorizeOrderRequest, OrderResponse>(
            HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize", request, idempotencyKey, cancellationToken);

        var authorization = response.PurchaseUnits?
            .SelectMany(pu => pu.Payments?.Authorizations ?? new List<AuthorizationDto>())
            .FirstOrDefault();

        if (authorization == null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new PayPalApiException(HttpStatusCode.BadGateway, "NO_AUTHORIZATION",
                $"PayPal order {payPalOrderId} was authorized but the response contained no authorization resource.", null);
        }

        _logger.LogInformation("PayPal authorization {AuthorizationId} for order {PayPalOrderId} has status {Status}",
            authorization.Id, payPalOrderId, authorization.Status);

        return MapAuthorization(authorization);
    }

    public async Task<PayPalAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<AuthorizationDto>(
            HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, cancellationToken);
        return MapAuthorization(response);
    }

    public async Task<PayPalAuthorizationInfo> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new ReauthorizeRequest
        {
            Amount = new MoneyDto { CurrencyCode = currency, Value = FormatAmount(amount) }
        };

        var response = await SendAsync<ReauthorizeRequest, AuthorizationDto>(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", request, idempotencyKey, cancellationToken);

        _logger.LogInformation("PayPal authorization {AuthorizationId} reauthorized with status {Status}",
            response.Id, response.Status);

        return MapAuthorization(response);
    }

    public async Task<PayPalCaptureInfo> CaptureAuthorizationAsync(string authorizationId, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new CaptureRequest { InvoiceId = invoiceId, FinalCapture = true };

        var response = await SendAsync<CaptureRequest, CaptureDto>(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", request, idempotencyKey, cancellationToken);

        _logger.LogInformation("PayPal capture {CaptureId} for authorization {AuthorizationId} has status {Status}",
            response.Id, authorizationId, response.Status);

        return new PayPalCaptureInfo
        {
            Id = response.Id ?? string.Empty,
            Status = response.Status ?? string.Empty,
            GrossAmount = ParseAmount(response.SellerReceivableBreakdown?.GrossAmount ?? response.Amount),
            PayPalFee = ParseNullableAmount(response.SellerReceivableBreakdown?.PayPalFee),
            NetAmount = ParseNullableAmount(response.SellerReceivableBreakdown?.NetAmount),
            Currency = (response.SellerReceivableBreakdown?.GrossAmount ?? response.Amount)?.CurrencyCode ?? string.Empty
        };
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await SendAsync<AuthorizationDto>(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, cancellationToken);

        _logger.LogInformation("PayPal authorization {AuthorizationId} voided", authorizationId);
    }

    public async Task<PayPalRefundInfo> RefundCaptureAsync(string captureId, decimal? amount, string currency, string invoiceId, string? noteToPayer, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new RefundRequest
        {
            Amount = amount.HasValue ? new MoneyDto { CurrencyCode = currency, Value = FormatAmount(amount.Value) } : null,
            InvoiceId = invoiceId,
            CustomId = invoiceId,
            NoteToPayer = noteToPayer
        };

        var response = await SendAsync<RefundRequest, RefundDto>(
            HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", request, idempotencyKey, cancellationToken);

        _logger.LogInformation("PayPal refund {RefundId} for capture {CaptureId} has status {Status}",
            response.Id, captureId, response.Status);

        return new PayPalRefundInfo
        {
            Id = response.Id ?? string.Empty,
            Status = response.Status ?? string.Empty,
            Amount = ParseAmount(response.Amount),
            Currency = response.Amount?.CurrencyCode ?? string.Empty
        };
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(GatewayCardDetails card, string customerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new CreatePaymentTokenRequest
        {
            PaymentSource = new PaymentTokenSource { Card = MapCard(card) },
            // customer.id is PayPal-generated; our own shopper reference goes in merchant_customer_id.
            Customer = new CustomerDto { MerchantCustomerId = customerId }
        };

        var response = await SendAsync<CreatePaymentTokenRequest, PaymentTokenResponse>(
            HttpMethod.Post, "/v3/vault/payment-tokens", request, idempotencyKey, cancellationToken);

        _logger.LogInformation("PayPal payment token {PaymentTokenId} created for customer", response.Id);

        return new PayPalVaultedCard
        {
            PaymentTokenId = response.Id ?? string.Empty,
            CustomerId = response.Customer?.Id,
            Brand = response.PaymentSource?.Card?.Brand,
            LastDigits = response.PaymentSource?.Card?.LastDigits,
            Expiry = response.PaymentSource?.Card?.Expiry,
            CardholderName = response.PaymentSource?.Card?.Name
        };
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{paymentTokenId}", null, cancellationToken);

        _logger.LogInformation("PayPal payment token {PaymentTokenId} deleted", paymentTokenId);
    }

    public async Task<PayPalTransactionPage> SearchTransactionsAsync(DateTimeOffset start, DateTimeOffset end, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var path = "/v1/reporting/transactions" +
            $"?start_date={Uri.EscapeDataString(start.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}" +
            $"&end_date={Uri.EscapeDataString(end.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}" +
            "&fields=transaction_info&balance_affecting_records_only=N" +
            $"&page={page}&page_size={pageSize}";

        var response = await SendAsync<TransactionSearchResponse>(HttpMethod.Get, path, null, cancellationToken);

        var page1 = new PayPalTransactionPage
        {
            Page = response.Page ?? page,
            TotalPages = response.TotalPages ?? 1,
            TotalItems = response.TotalItems ?? 0
        };

        foreach (var detail in response.TransactionDetails ?? new List<TransactionDetailDto>())
        {
            var info = detail.TransactionInfo;
            if (info == null)
            {
                continue;
            }

            page1.Transactions.Add(new PayPalTransaction
            {
                TransactionId = info.TransactionId ?? string.Empty,
                ReferenceId = info.PayPalReferenceId,
                ReferenceIdType = info.PayPalReferenceIdType,
                EventCode = info.TransactionEventCode,
                Status = info.TransactionStatus,
                Amount = ParseNullableAmount(info.TransactionAmount),
                Currency = info.TransactionAmount?.CurrencyCode,
                Fee = ParseNullableAmount(info.FeeAmount),
                InvoiceId = info.InvoiceId,
                CustomField = info.CustomField,
                InitiationDate = ParseDate(info.TransactionInitiationDate),
                UpdatedDate = ParseDate(info.TransactionUpdatedDate)
            });
        }

        return page1;
    }

    private static PaymentSourceDto BuildPaymentSource(GatewayPaymentSource paymentSource)
    {
        if (!string.IsNullOrEmpty(paymentSource.VaultTokenId))
        {
            return new PaymentSourceDto
            {
                Card = new CardDto
                {
                    VaultId = paymentSource.VaultTokenId,
                    StoredCredential = new StoredCredentialDto
                    {
                        PaymentInitiator = "CUSTOMER",
                        PaymentType = "ONE_TIME"
                    }
                }
            };
        }

        if (paymentSource.Card == null)
        {
            throw new ArgumentException("A payment source requires either card details or a vault token id.", nameof(paymentSource));
        }

        return new PaymentSourceDto { Card = MapCard(paymentSource.Card) };
    }

    private static CardDto MapCard(GatewayCardDetails card) => new()
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        Name = card.CardholderName,
        BillingAddress = card.BillingAddress == null ? null : new AddressDto
        {
            AddressLine1 = card.BillingAddress.AddressLine1,
            AddressLine2 = card.BillingAddress.AddressLine2,
            AdminArea2 = card.BillingAddress.City,
            AdminArea1 = card.BillingAddress.State,
            PostalCode = card.BillingAddress.PostalCode,
            CountryCode = card.BillingAddress.CountryCode
        }
    };

    private static PayPalAuthorizationInfo MapAuthorization(AuthorizationDto authorization) => new()
    {
        Id = authorization.Id ?? string.Empty,
        Status = authorization.Status ?? string.Empty,
        Amount = ParseAmount(authorization.Amount),
        Currency = authorization.Amount?.CurrencyCode ?? string.Empty,
        ExpirationTime = ParseDate(authorization.ExpirationTime)
    };

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseAmount(MoneyDto? money) =>
        money?.Value == null ? 0m : decimal.Parse(money.Value, CultureInfo.InvariantCulture);

    private static decimal? ParseNullableAmount(MoneyDto? money) =>
        money?.Value == null ? null : decimal.Parse(money.Value, CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        string.IsNullOrEmpty(value) ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private async Task<TResponse> SendAsync<TRequest, TResponse>(HttpMethod method, string path, TRequest? body, string? idempotencyKey, CancellationToken cancellationToken)
        where TResponse : class
    {
        var response = await SendAsync(method, path, body == null ? null : JsonSerializer.Serialize(body, JsonOptions), idempotencyKey, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ToException(response.StatusCode, content);
        }

        return JsonSerializer.Deserialize<TResponse>(content, JsonOptions)
            ?? throw new PayPalApiException(HttpStatusCode.BadGateway, "EMPTY_RESPONSE", "PayPal returned an empty response body.", null);
    }

    private async Task<TResponse> SendAsync<TResponse>(HttpMethod method, string path, string? idempotencyKey, CancellationToken cancellationToken)
        where TResponse : class
    {
        var response = await SendAsync(method, path, null, idempotencyKey, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ToException(response.StatusCode, content);
        }

        return JsonSerializer.Deserialize<TResponse>(content, JsonOptions)
            ?? throw new PayPalApiException(HttpStatusCode.BadGateway, "EMPTY_RESPONSE", "PayPal returned an empty response body.", null);
    }

    private async Task SendAsync(HttpMethod method, string path, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var response = await SendAsync(method, path, null, idempotencyKey, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            throw ToException(response.StatusCode, content);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? jsonBody, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (jsonBody != null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return await _httpClient.SendAsync(request, cancellationToken);
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

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw ToException(response.StatusCode, content);
            }

            var token = JsonSerializer.Deserialize<TokenResponse>(content, JsonOptions)
                ?? throw new PayPalApiException(HttpStatusCode.BadGateway, "EMPTY_RESPONSE", "PayPal returned an empty token response.", null);

            _accessToken = token.AccessToken ?? throw new PayPalApiException(HttpStatusCode.BadGateway, "NO_TOKEN", "PayPal token response contained no access token.", null);
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn - 60);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static PayPalApiException ToException(HttpStatusCode statusCode, string content)
    {
        try
        {
            var error = JsonSerializer.Deserialize<ErrorResponse>(content, JsonOptions);
            var issues = error?.Details?
                .Select(d => string.IsNullOrEmpty(d.Description) ? d.Issue ?? string.Empty : $"{d.Issue}: {d.Description}")
                .Where(i => !string.IsNullOrEmpty(i))
                .ToList();
            var message = error?.Message ?? $"PayPal request failed with status {(int)statusCode}.";
            if (issues is { Count: > 0 })
            {
                message = $"{message} [{string.Join("; ", issues)}]";
            }
            return new PayPalApiException(statusCode, error?.Name, message, error?.DebugId, issues);
        }
        catch (JsonException)
        {
            return new PayPalApiException(statusCode, null, $"PayPal request failed with status {(int)statusCode}.", null);
        }
    }

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
        public string? Description { get; set; }
    }

    private sealed class MoneyDto
    {
        public string? CurrencyCode { get; set; }
        public string? Value { get; set; }
    }

    private sealed class CreateOrderRequest
    {
        public string Intent { get; set; } = string.Empty;
        public List<PurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    }

    private sealed class PurchaseUnitRequest
    {
        public string? ReferenceId { get; set; }
        public string? CustomId { get; set; }
        public string? InvoiceId { get; set; }
        public MoneyDto? Amount { get; set; }
    }

    private sealed class AuthorizeOrderRequest
    {
        public PaymentSourceDto? PaymentSource { get; set; }
    }

    private sealed class PaymentSourceDto
    {
        public CardDto? Card { get; set; }
    }

    private sealed class CardDto
    {
        public string? Number { get; set; }
        public string? Expiry { get; set; }
        public string? SecurityCode { get; set; }
        public string? Name { get; set; }
        public AddressDto? BillingAddress { get; set; }
        public string? VaultId { get; set; }
        public StoredCredentialDto? StoredCredential { get; set; }
    }

    private sealed class StoredCredentialDto
    {
        public string? PaymentInitiator { get; set; }
        public string? PaymentType { get; set; }
    }

    private sealed class AddressDto
    {
        [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
        [JsonPropertyName("address_line_2")] public string? AddressLine2 { get; set; }
        [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; }
        [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; }
        public string? PostalCode { get; set; }
        public string? CountryCode { get; set; }
    }

    private sealed class OrderResponse
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public List<PurchaseUnitResponse>? PurchaseUnits { get; set; }
    }

    private sealed class PurchaseUnitResponse
    {
        public PaymentsDto? Payments { get; set; }
    }

    private sealed class PaymentsDto
    {
        public List<AuthorizationDto>? Authorizations { get; set; }
    }

    private sealed class AuthorizationDto
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public MoneyDto? Amount { get; set; }
        public string? ExpirationTime { get; set; }
    }

    private sealed class ReauthorizeRequest
    {
        public MoneyDto? Amount { get; set; }
    }

    private sealed class CaptureRequest
    {
        public string? InvoiceId { get; set; }
        public bool FinalCapture { get; set; }
    }

    private sealed class CaptureDto
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public MoneyDto? Amount { get; set; }
        public SellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }
    }

    private sealed class SellerReceivableBreakdownDto
    {
        public MoneyDto? GrossAmount { get; set; }
        [JsonPropertyName("paypal_fee")] public MoneyDto? PayPalFee { get; set; }
        public MoneyDto? NetAmount { get; set; }
    }

    private sealed class RefundRequest
    {
        public MoneyDto? Amount { get; set; }
        public string? InvoiceId { get; set; }
        public string? CustomId { get; set; }
        public string? NoteToPayer { get; set; }
    }

    private sealed class RefundDto
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public MoneyDto? Amount { get; set; }
    }

    private sealed class CreatePaymentTokenRequest
    {
        public PaymentTokenSource? PaymentSource { get; set; }
        public CustomerDto? Customer { get; set; }
    }

    private sealed class PaymentTokenSource
    {
        public CardDto? Card { get; set; }
    }

    private sealed class CustomerDto
    {
        public string? Id { get; set; }
        public string? MerchantCustomerId { get; set; }
    }

    private sealed class PaymentTokenResponse
    {
        public string? Id { get; set; }
        public CustomerDto? Customer { get; set; }
        public PaymentTokenResponseSource? PaymentSource { get; set; }
    }

    private sealed class PaymentTokenResponseSource
    {
        public VaultedCardDto? Card { get; set; }
    }

    private sealed class VaultedCardDto
    {
        public string? Brand { get; set; }
        public string? LastDigits { get; set; }
        public string? Expiry { get; set; }
        public string? Name { get; set; }
    }

    private sealed class TransactionSearchResponse
    {
        public List<TransactionDetailDto>? TransactionDetails { get; set; }
        public int? Page { get; set; }
        public int? TotalPages { get; set; }
        public int? TotalItems { get; set; }
    }

    private sealed class TransactionDetailDto
    {
        public TransactionInfoDto? TransactionInfo { get; set; }
    }

    private sealed class TransactionInfoDto
    {
        public string? TransactionId { get; set; }
        [JsonPropertyName("paypal_reference_id")] public string? PayPalReferenceId { get; set; }
        [JsonPropertyName("paypal_reference_id_type")] public string? PayPalReferenceIdType { get; set; }
        public string? TransactionEventCode { get; set; }
        public string? TransactionStatus { get; set; }
        public MoneyDto? TransactionAmount { get; set; }
        public MoneyDto? FeeAmount { get; set; }
        public string? InvoiceId { get; set; }
        public string? CustomField { get; set; }
        public string? TransactionInitiationDate { get; set; }
        public string? TransactionUpdatedDate { get; set; }
    }
}

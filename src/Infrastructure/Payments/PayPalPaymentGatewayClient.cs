using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal implementation of the payment gateway. Talks to the Orders v2, Payments v2,
/// Vault v3 and Transaction Search v1 APIs. Card details flow through requests only;
/// they are never persisted and never written to logs.
/// </summary>
public class PayPalPaymentGatewayClient : IPaymentGatewayClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentGatewayClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalPaymentGatewayClient(HttpClient http, IOptions<PayPalSettings> settings, ILogger<PayPalPaymentGatewayClient> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                "(e.g. via user-secrets from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables).");
        }

        _http.BaseAddress = new Uri(_settings.ResolveBaseUrl() + "/");
    }

    public async Task<string> CreateOrderAsync(decimal amount, string currency, string referenceId, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new PayPalCreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequest>
            {
                new()
                {
                    ReferenceId = referenceId,
                    CustomId = referenceId,
                    InvoiceId = invoiceId,
                    Amount = Money(amount, currency)
                }
            }
        };

        var response = await SendAsync<PayPalOrderResponse>(HttpMethod.Post, "v2/checkout/orders", request, idempotencyKey, cancellationToken);
        return response.Id ?? throw new PaymentGatewayException(System.Net.HttpStatusCode.BadGateway, null, "PayPal did not return an order id.");
    }

    public Task<GatewayAuthorization> AuthorizeWithCardAsync(string gatewayOrderId, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new PayPalAuthorizeOrderRequest
        {
            PaymentSource = new PayPalPaymentSourceRequest { Card = ToCardRequest(card) }
        };
        return AuthorizeAsync(gatewayOrderId, request, idempotencyKey, cancellationToken);
    }

    public Task<GatewayAuthorization> AuthorizeWithVaultedCardAsync(string gatewayOrderId, string paymentTokenId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new PayPalAuthorizeOrderRequest
        {
            PaymentSource = new PayPalPaymentSourceRequest
            {
                Card = new PayPalCardRequest
                {
                    VaultId = paymentTokenId,
                    StoredCredential = new PayPalStoredCredential { PaymentInitiator = "CUSTOMER", PaymentType = "ONE_TIME" }
                }
            }
        };
        return AuthorizeAsync(gatewayOrderId, request, idempotencyKey, cancellationToken);
    }

    private async Task<GatewayAuthorization> AuthorizeAsync(string gatewayOrderId, PayPalAuthorizeOrderRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        var response = await SendAsync<PayPalOrderResponse>(HttpMethod.Post, $"v2/checkout/orders/{gatewayOrderId}/authorize", request, idempotencyKey, cancellationToken);
        var authorization = response.PurchaseUnits?.SelectMany(p => p.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorization>()).FirstOrDefault();
        if (authorization?.Id is null)
        {
            throw new PaymentGatewayException(System.Net.HttpStatusCode.BadGateway, null,
                $"PayPal order {gatewayOrderId} was authorized but no authorization details were returned.");
        }
        return Map(authorization);
    }

    public async Task<GatewayAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<PayPalAuthorization>(HttpMethod.Get, $"v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        return Map(response);
    }

    public async Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // No invoice_id is sent: the capture then inherits the (unique) invoice id of the
        // authorizing transaction, and the merchant account's invoice-uniqueness rule is
        // never tripped by captures.
        var request = new PayPalCaptureRequest { Amount = Money(amount, currency), FinalCapture = true };
        var response = await SendAsync<PayPalCapture>(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/capture", request, idempotencyKey, cancellationToken);
        return Map(response);
    }

    public async Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new PayPalReauthorizeRequest { Amount = Money(amount, currency) };
        var response = await SendAsync<PayPalAuthorization>(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/reauthorize", request, idempotencyKey, cancellationToken);
        return Map(response);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await SendAsync<PayPalAuthorization>(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void", null, idempotencyKey, cancellationToken);
    }

    public async Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, string? note, CancellationToken cancellationToken = default)
    {
        var request = new PayPalRefundRequest
        {
            Amount = amount.HasValue ? Money(amount.Value, currency) : null,
            NoteToPayer = note
        };
        var response = await SendAsync<PayPalRefund>(HttpMethod.Post, $"v2/payments/captures/{captureId}/refund", request, idempotencyKey, cancellationToken);
        return new GatewayRefund
        {
            Id = response.Id ?? string.Empty,
            Status = response.Status ?? string.Empty,
            Amount = ParseMoney(response.Amount).amount,
            Currency = ParseMoney(response.Amount).currency
        };
    }

    public async Task<GatewayVaultedCard> VaultCardAsync(CardDetails card, string customerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var setupRequest = new PayPalSetupTokenRequest
        {
            PaymentSource = new PayPalPaymentSourceRequest { Card = ToCardRequest(card) }
        };
        var setupToken = await SendAsync<PayPalSetupTokenResponse>(HttpMethod.Post, "v3/vault/setup-tokens", setupRequest, idempotencyKey, cancellationToken);
        if (setupToken.Id is null)
        {
            throw new PaymentGatewayException(System.Net.HttpStatusCode.BadGateway, null, "PayPal did not return a setup token.");
        }

        var tokenRequest = new PayPalPaymentTokenRequest
        {
            PaymentSource = new PayPalPaymentSourceRequest
            {
                Token = new PayPalTokenReference { Id = setupToken.Id, Type = "SETUP_TOKEN" }
            },
            Customer = new PayPalCustomer { Id = customerId }
        };
        var paymentToken = await SendAsync<PayPalPaymentTokenResponse>(HttpMethod.Post, "v3/vault/payment-tokens", tokenRequest, idempotencyKey + "-pt", cancellationToken);
        if (paymentToken.Id is null)
        {
            throw new PaymentGatewayException(System.Net.HttpStatusCode.BadGateway, null, "PayPal did not return a payment token.");
        }

        return new GatewayVaultedCard
        {
            PaymentTokenId = paymentToken.Id,
            CustomerId = paymentToken.Customer?.Id ?? customerId,
            Brand = paymentToken.PaymentSource?.Card?.Brand,
            LastDigits = paymentToken.PaymentSource?.Card?.LastDigits,
            Expiry = paymentToken.PaymentSource?.Card?.Expiry,
            CardholderName = paymentToken.PaymentSource?.Card?.Name
        };
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"v3/vault/payment-tokens/{paymentTokenId}", null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<GatewayTransaction>();
        var windowStart = from;
        while (windowStart < to)
        {
            // The API supports a maximum range of 31 days per request.
            var windowEnd = windowStart.AddDays(31) < to ? windowStart.AddDays(31) : to;
            var page = 1;
            var totalPages = 1;
            while (page <= totalPages)
            {
                var query = $"v1/reporting/transactions?start_date={FormatInstant(windowStart)}&end_date={FormatInstant(windowEnd)}" +
                            $"&fields=all&balance_affecting_records_only=N&page_size=100&page={page}&total_required=true";
                var response = await SendAsync<PayPalTransactionSearchResponse>(HttpMethod.Get, query, null, null, cancellationToken);
                foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<PayPalTransactionDetail>())
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null) continue;
                    results.Add(new GatewayTransaction
                    {
                        TransactionId = info.TransactionId,
                        ReferenceId = info.PayPalReferenceId,
                        ReferenceIdType = info.PayPalReferenceIdType,
                        EventCode = info.TransactionEventCode,
                        Status = info.TransactionStatus,
                        Amount = info.TransactionAmount is null ? null : ParseMoney(info.TransactionAmount).amount,
                        Fee = info.FeeAmount is null ? null : ParseMoney(info.FeeAmount).amount,
                        Currency = info.TransactionAmount?.CurrencyCode,
                        InitiationTime = ParseInstant(info.TransactionInitiationDate),
                        UpdatedTime = ParseInstant(info.TransactionUpdatedDate),
                        InvoiceId = info.InvoiceId,
                        CustomId = info.CustomField
                    });
                }
                totalPages = response.TotalPages > 0 ? response.TotalPages : 1;
                page++;
            }
            windowStart = windowEnd;
        }
        return results;
    }

    private static PayPalCardRequest ToCardRequest(CardDetails card) => new()
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        Name = card.CardholderName,
        BillingAddress = new PayPalAddress
        {
            AddressLine1 = card.AddressLine1,
            AddressLine2 = card.AddressLine2,
            City = card.City,
            State = card.State,
            PostalCode = card.PostalCode,
            CountryCode = card.CountryCode
        }
    };

    private static PayPalMoney Money(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static (decimal amount, string currency) ParseMoney(PayPalMoney? money) =>
        (money?.Value is null ? 0m : decimal.Parse(money.Value, CultureInfo.InvariantCulture), money?.CurrencyCode ?? string.Empty);

    private static DateTimeOffset? ParseInstant(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;

    private static string FormatInstant(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static GatewayAuthorization Map(PayPalAuthorization authorization) => new()
    {
        Id = authorization.Id ?? string.Empty,
        Status = authorization.Status ?? string.Empty,
        Amount = ParseMoney(authorization.Amount).amount,
        Currency = ParseMoney(authorization.Amount).currency,
        ExpirationTime = ParseInstant(authorization.ExpirationTime)
    };

    private static GatewayCapture Map(PayPalCapture capture)
    {
        var breakdown = capture.SellerReceivableBreakdown;
        return new GatewayCapture
        {
            Id = capture.Id ?? string.Empty,
            Status = capture.Status ?? string.Empty,
            GrossAmount = breakdown?.GrossAmount is not null ? ParseMoney(breakdown.GrossAmount).amount : ParseMoney(capture.Amount).amount,
            PayPalFee = breakdown?.PayPalFee is null ? null : ParseMoney(breakdown.PayPalFee).amount,
            NetAmount = breakdown?.NetAmount is null ? null : ParseMoney(breakdown.NetAmount).amount,
            Currency = ParseMoney(capture.Amount).currency
        };
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, body, idempotencyKey, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return Activator.CreateInstance<T>();
        }
        return JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new PaymentGatewayException(System.Net.HttpStatusCode.BadGateway, null, $"PayPal returned an unreadable response for {method} {path}.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (body is not null)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        // Never log request/response bodies here: they can contain card data.
        var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            PayPalErrorResponse? error = null;
            try { error = JsonSerializer.Deserialize<PayPalErrorResponse>(errorContent, JsonOptions); }
            catch (JsonException) { /* non-JSON error body */ }

            var detail = error?.Details?.FirstOrDefault();
            var message = error?.Message ?? $"PayPal request {method} {path} failed with status {(int)response.StatusCode}.";
            if (detail?.Description is not null)
            {
                message = $"{message} ({detail.Issue}: {detail.Description})";
            }
            _logger.LogWarning("PayPal {Method} {Path} failed: {Status} {Name} (debug id {DebugId})",
                method, path, (int)response.StatusCode, error?.Name, error?.DebugId);
            throw new PaymentGatewayException(response.StatusCode, error?.Name, message, error?.DebugId);
        }
        return response;
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
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _http.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new PaymentGatewayException(response.StatusCode, null, "PayPal rejected the client credentials; check PayPal:ClientId / PayPal:ClientSecret.");
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(content, JsonOptions);
            if (token?.AccessToken is null)
            {
                throw new PaymentGatewayException(System.Net.HttpStatusCode.BadGateway, null, "PayPal did not return an access token.");
            }

            _accessToken = token.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn).AddMinutes(-1);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}

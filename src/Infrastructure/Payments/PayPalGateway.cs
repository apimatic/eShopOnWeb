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
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Hand-written PayPal client built against the OpenAPI contracts in api-specs/paypal/:
/// checkout_orders_v2, payments_payment_v2, vault_payment_tokens_v3 and
/// transaction_search_v1. Authentication uses the OAuth2 client-credentials flow those
/// specs declare (tokenUrl /v1/oauth2/token).
/// </summary>
public class PayPalGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalGateway> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public PayPalGateway(HttpClient httpClient, PayPalSettings settings, ILogger<PayPalGateway> logger)
    {
        _settings = settings;
        _settings.Validate();
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(_settings.ApiBaseUrl + "/");
        _logger = logger;
    }

    // ---- Orders / authorizations ----

    public async Task<GatewayOrder> CreateOrderAsync(decimal amount, string currencyCode, string referenceId, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new PayPalCreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequest>
            {
                new()
                {
                    ReferenceId = referenceId,
                    CustomId = invoiceId,
                    InvoiceId = invoiceId,
                    Description = $"eShopOnWeb order {referenceId}",
                    Amount = Money(amount, currencyCode)
                }
            }
        };

        _logger.LogInformation("Creating PayPal order: {Amount} {Currency}, invoice {InvoiceId}", amount, currencyCode, invoiceId);
        var response = await SendAsync<PayPalOrderResponse>(HttpMethod.Post, "v2/checkout/orders", request, idempotencyKey, cancellationToken);
        _logger.LogInformation("PayPal order {OrderId} created (status {Status})", response.Id, response.Status);
        return new GatewayOrder { Id = response.Id ?? string.Empty, Status = response.Status ?? string.Empty };
    }

    public async Task<GatewayAuthorization> AuthorizeOrderAsync(string gatewayOrderId, GatewayPaymentSource paymentSource, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new PayPalAuthorizeOrderRequest
        {
            PaymentSource = new PayPalPaymentSourceRequest { Card = BuildCardRequest(paymentSource) }
        };

        _logger.LogInformation("Authorizing PayPal order {OrderId} (source: {Source})",
            gatewayOrderId, paymentSource.VaultTokenId is not null ? "vaulted card" : "card");
        var response = await SendAsync<PayPalOrderResponse>(HttpMethod.Post,
            $"v2/checkout/orders/{Uri.EscapeDataString(gatewayOrderId)}/authorize", request, idempotencyKey, cancellationToken);

        if (response.Links?.Any(l => l.Rel == "payer-action") == true)
        {
            throw new PayerActionRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (payer-action challenge), which this integration does not perform.");
        }

        var authorization = response.PurchaseUnits?.SelectMany(u => u.Payments?.Authorizations ?? new List<PayPalAuthorization>())
            .FirstOrDefault();
        if (authorization?.Id is null)
        {
            throw new PaymentGatewayException(502, null,
                $"PayPal order {gatewayOrderId} was authorized but the response contained no authorization resource (order status {response.Status}).");
        }

        var mapped = MapAuthorization(authorization);
        mapped.CardBrand = response.PaymentSource?.Card?.Brand;
        mapped.CardLastDigits = response.PaymentSource?.Card?.LastDigits;
        return mapped;
    }

    public async Task<GatewayAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<PayPalAuthorization>(HttpMethod.Get,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null, cancellationToken);
        return MapAuthorization(response);
    }

    public async Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new PayPalReauthorizeRequest { Amount = Money(amount, currencyCode) };
        var response = await SendAsync<PayPalAuthorization>(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize", request, idempotencyKey, cancellationToken);
        return MapAuthorization(response);
    }

    public async Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new PayPalCaptureRequest { Amount = Money(amount, currencyCode), FinalCapture = true };
        var response = await SendAsync<PayPalCapture>(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture", request, idempotencyKey, cancellationToken);

        return new GatewayCapture
        {
            CaptureId = response.Id ?? string.Empty,
            Status = response.Status ?? string.Empty,
            Amount = ParseMoney(response.Amount).amount,
            CurrencyCode = ParseMoney(response.Amount).currency,
            Fee = response.SellerReceivableBreakdown?.PayPalFee is null ? null : ParseMoney(response.SellerReceivableBreakdown.PayPalFee).amount,
            NetAmount = response.SellerReceivableBreakdown?.NetAmount is null ? null : ParseMoney(response.SellerReceivableBreakdown.NetAmount).amount
        };
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await SendAsync<PayPalAuthorization>(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void", null, idempotencyKey, cancellationToken);
    }

    public async Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new PayPalRefundRequest
        {
            Amount = amount.HasValue ? Money(amount.Value, currencyCode) : null,
            CustomId = idempotencyKey
        };
        var response = await SendAsync<PayPalRefund>(HttpMethod.Post,
            $"v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund", request, idempotencyKey, cancellationToken);

        return new GatewayRefund
        {
            RefundId = response.Id ?? string.Empty,
            Status = response.Status ?? string.Empty,
            Amount = ParseMoney(response.Amount).amount,
            CurrencyCode = ParseMoney(response.Amount).currency
        };
    }

    // ---- Vault ----

    public async Task<GatewaySavedCard> SaveCardAsync(GatewayCard card, string customerId, CancellationToken cancellationToken = default)
    {
        var request = new PayPalVaultTokenRequest
        {
            PaymentSource = new PayPalPaymentSourceRequest
            {
                Card = new PayPalCardRequest
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            },
            Customer = new PayPalVaultCustomer { Id = customerId }
        };

        var response = await SendAsync<PayPalVaultTokenResponse>(HttpMethod.Post, "v3/vault/payment-tokens", request, null, cancellationToken);
        return MapSavedCard(response);
    }

    public async Task<IReadOnlyList<GatewaySavedCard>> ListSavedCardsAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var cards = new List<GatewaySavedCard>();
        var page = 1;
        const int pageSize = 50;
        while (true)
        {
            var response = await SendAsync<PayPalVaultListResponse>(HttpMethod.Get,
                $"v3/vault/payment-tokens?customer_id={Uri.EscapeDataString(customerId)}&page_size={pageSize}&page={page}&total_required=true",
                null, null, cancellationToken);

            if (response.PaymentTokens is not null)
            {
                cards.AddRange(response.PaymentTokens.Select(MapSavedCard));
            }

            if (response.TotalPages <= page || response.PaymentTokens is null || response.PaymentTokens.Count == 0)
            {
                break;
            }
            page++;
        }
        return cards;
    }

    public async Task DeleteSavedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(HttpMethod.Delete,
            $"v3/vault/payment-tokens/{Uri.EscapeDataString(vaultTokenId)}", null, null, cancellationToken);
    }

    // ---- Transaction search ----

    public async Task<GatewayTransactionPage> GetTransactionsAsync(DateTimeOffset from, DateTimeOffset to, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = string.Join('&', new[]
        {
            $"start_date={Uri.EscapeDataString(FormatTransactionDate(from))}",
            $"end_date={Uri.EscapeDataString(FormatTransactionDate(to))}",
            "fields=all",
            $"page_size={pageSize}",
            $"page={page}"
        });

        var response = await SendAsync<PayPalTransactionSearchResponse>(HttpMethod.Get,
            $"v1/reporting/transactions?{query}", null, null, cancellationToken);

        return new GatewayTransactionPage
        {
            Page = response.Page,
            TotalPages = response.TotalPages,
            TotalItems = response.TotalItems,
            Transactions = (response.TransactionDetails ?? new List<PayPalTransactionDetail>())
                .Select(d => d.TransactionInfo)
                .Where(t => t is not null)
                .Select(t => new GatewayTransaction
                {
                    TransactionId = t!.TransactionId,
                    ReferenceId = t.PayPalReferenceId,
                    ReferenceIdType = t.PayPalReferenceIdType,
                    EventCode = t.TransactionEventCode,
                    Status = t.TransactionStatus,
                    Amount = t.TransactionAmount is null ? null : ParseMoney(t.TransactionAmount).amount,
                    CurrencyCode = t.TransactionAmount?.CurrencyCode,
                    FeeAmount = t.FeeAmount is null ? null : ParseMoney(t.FeeAmount).amount,
                    InitiationTime = ParseDate(t.TransactionInitiationDate),
                    UpdatedTime = ParseDate(t.TransactionUpdatedDate),
                    InvoiceId = t.InvoiceId,
                    CustomField = t.CustomField
                })
                .ToList()
        };
    }

    // ---- Helpers ----

    private static string FormatTransactionDate(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture);

    private static PayPalCardRequest BuildCardRequest(GatewayPaymentSource source)
    {
        if (source.VaultTokenId is not null)
        {
            return new PayPalCardRequest
            {
                VaultId = source.VaultTokenId,
                StoredCredential = new PayPalStoredCredential
                {
                    PaymentInitiator = "CUSTOMER",
                    PaymentType = "ONE_TIME",
                    Usage = "SUBSEQUENT"
                }
            };
        }

        var card = source.Card ?? throw new ArgumentException("A payment source is required.", nameof(source));
        return new PayPalCardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = MapAddress(card.BillingAddress)
        };
    }

    private static PayPalAddress? MapAddress(GatewayCardAddress? address)
    {
        if (address is null) return null;
        return new PayPalAddress
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static GatewayAuthorization MapAuthorization(PayPalAuthorization authorization)
    {
        var (amount, currency) = ParseMoney(authorization.Amount);
        return new GatewayAuthorization
        {
            AuthorizationId = authorization.Id ?? string.Empty,
            Status = authorization.Status ?? string.Empty,
            Amount = amount,
            CurrencyCode = currency,
            ExpiryTime = ParseDate(authorization.ExpirationTime)
        };
    }

    private static GatewaySavedCard MapSavedCard(PayPalVaultTokenResponse token)
    {
        var card = token.PaymentSource?.Card;
        return new GatewaySavedCard
        {
            VaultTokenId = token.Id ?? string.Empty,
            Brand = card?.Brand,
            LastDigits = card?.LastDigits,
            Expiry = card?.Expiry,
            CardholderName = card?.Name
        };
    }

    private static PayPalMoney Money(decimal amount, string currencyCode)
        => new() { CurrencyCode = currencyCode, Value = amount.ToString("0.00", CultureInfo.InvariantCulture) };

    private static (decimal amount, string currency) ParseMoney(PayPalMoney? money)
    {
        if (money?.Value is null) return (0m, string.Empty);
        return (decimal.Parse(money.Value, CultureInfo.InvariantCulture), money.CurrencyCode ?? string.Empty);
    }

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

    private async Task<TResponse> SendAsync<TResponse>(HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        if (method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            // The specs default to a minimal representation; we need the full resource.
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, body.GetType(), JsonOptions), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ToGatewayException((int)response.StatusCode, content);
        }

        if (string.IsNullOrWhiteSpace(content) || typeof(TResponse) == typeof(object))
        {
            return default!;
        }
        return JsonSerializer.Deserialize<TResponse>(content)
               ?? throw new PaymentGatewayException(502, null, $"PayPal returned an empty response for {method} {path}.");
    }

    private PaymentGatewayException ToGatewayException(int statusCode, string content)
    {
        try
        {
            var error = JsonSerializer.Deserialize<PayPalErrorResponse>(content);
            if (error is not null)
            {
                var detail = error.Details?.FirstOrDefault();
                var message = error.Message ?? "PayPal rejected the request.";
                if (detail?.Description is not null)
                {
                    message = $"{message} ({detail.Issue}: {detail.Description})";
                }
                _logger.LogWarning("PayPal error {StatusCode} {Name}: {Message} (debug id {DebugId})",
                    statusCode, error.Name, message, error.DebugId);
                return new PaymentGatewayException(statusCode, error.Name, message, error.DebugId);
            }
        }
        catch (JsonException)
        {
            // fall through to the generic error below
        }
        return new PaymentGatewayException(statusCode, null, $"PayPal call failed with HTTP {statusCode}.");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiry)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiry)
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw ToGatewayException((int)response.StatusCode, content);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(content);
            if (token?.AccessToken is null)
            {
                throw new PaymentGatewayException(502, null, "PayPal did not return an access token.");
            }

            _accessToken = token.AccessToken;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn - 60, 30));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}

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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Hand-written PayPal client built against the OpenAPI specifications in api-specs/paypal.
/// Endpoints, schemas, auth scheme (OAuth2 client credentials, token URL /v1/oauth2/token)
/// and server templating all come from those documents.
///
/// Full card details pass through this client to PayPal only; they are never logged here.
/// </summary>
public class PayPalClient : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalClient> _logger;
    private readonly string _baseUrl;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalSettings> settings, ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _baseUrl = _settings.ResolveBaseUrl();
    }

    public async Task<GatewayOrder> CreateOrderAsync(decimal amount, string currency, string customId, string invoiceId,
        CardDetails? card, string? vaultTokenId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new CreateOrderRequestWire
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PurchaseUnitRequestWire>
            {
                new()
                {
                    ReferenceId = customId,
                    CustomId = customId,
                    InvoiceId = invoiceId,
                    Description = $"eShopOnWeb {customId}",
                    Amount = new MoneyWire(currency, FormatAmount(amount))
                }
            }
        };

        if (card is not null)
        {
            request.PaymentSource = new PaymentSourceRequestWire { Card = ToCardWire(card) };
        }
        else if (vaultTokenId is not null)
        {
            // Paying with a vaulted card: reference the vault token; the shopper is present (CIT).
            request.PaymentSource = new PaymentSourceRequestWire
            {
                Card = new CardRequestWire
                {
                    VaultId = vaultTokenId,
                    StoredCredential = new StoredCredentialWire()
                }
            };
        }

        var response = await SendAsync<OrderResponseWire>(HttpMethod.Post, "/v2/checkout/orders", request, idempotencyKey, cancellationToken);

        // With a card payment source, PayPal authorizes the card during order creation;
        // the authorization is then already present on the response.
        var authorization = response.PurchaseUnits?
            .SelectMany(p => p.Payments?.Authorizations ?? Enumerable.Empty<AuthorizationWire>())
            .FirstOrDefault();

        return new GatewayOrder
        {
            Id = response.Id ?? string.Empty,
            Status = response.Status ?? string.Empty,
            Authorization = authorization is null ? null : ToGatewayAuthorization(authorization),
            CardBrand = response.PaymentSource?.Card?.Brand,
            CardLastDigits = response.PaymentSource?.Card?.LastDigits
        };
    }

    public async Task<GatewayAuthorizeResult> AuthorizeOrderAsync(string gatewayOrderId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<OrderResponseWire>(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(gatewayOrderId)}/authorize",
            new { }, idempotencyKey, cancellationToken);

        var authorization = response.PurchaseUnits?
            .SelectMany(p => p.Payments?.Authorizations ?? Enumerable.Empty<AuthorizationWire>())
            .FirstOrDefault();

        return new GatewayAuthorizeResult
        {
            OrderId = response.Id ?? gatewayOrderId,
            OrderStatus = response.Status ?? string.Empty,
            Authorization = authorization is null ? null : ToGatewayAuthorization(authorization),
            CardBrand = response.PaymentSource?.Card?.Brand,
            CardLastDigits = response.PaymentSource?.Card?.LastDigits
        };
    }

    public async Task<GatewayAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<AuthorizationWire>(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null, cancellationToken);
        return ToGatewayAuthorization(response);
    }

    public async Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<AuthorizationWire>(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new ReauthorizeRequestWire { Amount = new MoneyWire(currency, FormatAmount(amount)) },
            idempotencyKey, cancellationToken);
        return ToGatewayAuthorization(response);
    }

    public async Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, string? invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<CaptureWire>(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new CaptureRequestWire { InvoiceId = invoiceId, FinalCapture = true },
            idempotencyKey, cancellationToken);

        return new GatewayCapture
        {
            Id = response.Id ?? string.Empty,
            Status = response.Status ?? string.Empty,
            GrossAmount = ParseAmount(response.SellerReceivableBreakdown?.GrossAmount ?? response.Amount),
            Currency = (response.SellerReceivableBreakdown?.GrossAmount ?? response.Amount)?.CurrencyCode ?? string.Empty,
            PayPalFee = response.SellerReceivableBreakdown?.PayPalFee is null ? null : ParseAmount(response.SellerReceivableBreakdown.PayPalFee),
            NetAmount = response.SellerReceivableBreakdown?.NetAmount is null ? null : ParseAmount(response.SellerReceivableBreakdown.NetAmount)
        };
    }

    public async Task<GatewayAuthorization> VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<AuthorizationWire>(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            new { }, idempotencyKey, cancellationToken);

        return response is null
            ? new GatewayAuthorization { Id = authorizationId, Status = "VOIDED" }
            : ToGatewayAuthorization(response);
    }

    public async Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency, string? noteToPayer, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new RefundRequestWire
        {
            Amount = amount is null ? null : new MoneyWire(currency, FormatAmount(amount.Value)),
            NoteToPayer = noteToPayer,
            CustomId = idempotencyKey.Length <= 127 ? idempotencyKey : idempotencyKey[..127]
        };

        var response = await SendAsync<RefundWire>(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            request, idempotencyKey, cancellationToken);

        return new GatewayRefund
        {
            Id = response.Id ?? string.Empty,
            Status = response.Status ?? string.Empty,
            Amount = ParseAmount(response.Amount),
            Currency = response.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<GatewayVaultToken> CreateVaultTokenAsync(CardDetails card, string customerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new VaultTokenRequestWire
        {
            Customer = new VaultCustomerWire { Id = customerId },
            PaymentSource = new VaultPaymentSourceWire { Card = ToCardWire(card) }
        };

        var response = await SendAsync<VaultTokenResponseWire>(HttpMethod.Post, "/v3/vault/payment-tokens", request, idempotencyKey, cancellationToken);

        return new GatewayVaultToken
        {
            Id = response.Id ?? string.Empty,
            Brand = response.PaymentSource?.Card?.Brand,
            LastDigits = response.PaymentSource?.Card?.LastDigits,
            Expiry = response.PaymentSource?.Card?.Expiry,
            CardholderName = response.PaymentSource?.Card?.Name
        };
    }

    public async Task DeleteVaultTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultTokenId)}", null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<GatewayTransaction>();
        const int pageSize = 500; // contract maximum

        var startDate = from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var endDate = to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        var page = 1;
        while (true)
        {
            var path = "/v1/reporting/transactions"
                + $"?start_date={Uri.EscapeDataString(startDate)}"
                + $"&end_date={Uri.EscapeDataString(endDate)}"
                + "&fields=transaction_info"
                + $"&page_size={pageSize}&page={page}";

            var response = await SendAsync<TransactionSearchResponseWire>(HttpMethod.Get, path, null, null, cancellationToken);

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
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
                        Amount = info.TransactionAmount is null ? null : ParseAmount(info.TransactionAmount),
                        Currency = info.TransactionAmount?.CurrencyCode,
                        FeeAmount = info.FeeAmount is null ? null : ParseAmount(info.FeeAmount),
                        InvoiceId = info.InvoiceId,
                        CustomId = info.CustomField,
                        InitiationDate = ParseDateTime(info.TransactionInitiationDate)
                    });
                }
            }

            // Cover the whole range, not just the first page.
            if (page >= Math.Max(response.TotalPages, 1)) break;
            page++;
        }

        return results;
    }

    private static CardRequestWire ToCardWire(CardDetails card)
    {
        return new CardRequestWire
        {
            Name = card.CardholderName,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = card.BillingAddress is null ? null : new AddressWire
            {
                AddressLine1 = card.BillingAddress.AddressLine1,
                AddressLine2 = card.BillingAddress.AddressLine2,
                AdminArea2 = card.BillingAddress.City,
                AdminArea1 = card.BillingAddress.State,
                PostalCode = card.BillingAddress.PostalCode,
                CountryCode = card.BillingAddress.CountryCode
            }
        };
    }

    private static GatewayAuthorization ToGatewayAuthorization(AuthorizationWire wire)
    {
        return new GatewayAuthorization
        {
            Id = wire.Id ?? string.Empty,
            Status = wire.Status ?? string.Empty,
            Amount = ParseAmount(wire.Amount),
            Currency = wire.Amount?.CurrencyCode ?? string.Empty,
            ExpirationTime = ParseDateTime(wire.ExpirationTime)
        };
    }

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal ParseAmount(MoneyWire? money)
        => money is null ? 0m : decimal.Parse(money.Value, CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDateTime(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, _baseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ToGatewayException((int)response.StatusCode, content);
        }

        if (string.IsNullOrWhiteSpace(content) || typeof(T) == typeof(object))
        {
            return default!;
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions)!;
    }

    private PaymentGatewayException ToGatewayException(int statusCode, string content)
    {
        ErrorResponseWire? error = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                error = JsonSerializer.Deserialize<ErrorResponseWire>(content, JsonOptions);
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through to a generic gateway exception.
        }

        if (error is null)
        {
            _logger.LogWarning("PayPal call failed with HTTP {StatusCode}", statusCode);
            return new PaymentGatewayException(statusCode, null, "PayPal rejected the request.", null);
        }

        var issues = error.Details is null ? null : string.Join("; ", error.Details.Select(d => $"{d.Issue}: {d.Description}"));
        _logger.LogWarning("PayPal call failed: {ErrorName} {Message} {Issues} (debug id {DebugId})",
            error.Name, error.Message, issues, error.DebugId);
        return new PaymentGatewayException(statusCode, error.Name,
            issues is null ? error.Message ?? "PayPal rejected the request." : $"{error.Message} ({issues})",
            error.DebugId);
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

            // OAuth2 client-credentials flow; token URL /v1/oauth2/token per the spec's security scheme.
            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw ToGatewayException((int)response.StatusCode, content);
            }

            var token = JsonSerializer.Deserialize<TokenResponse>(content, JsonOptions);
            if (token?.AccessToken is null)
            {
                throw new PaymentGatewayException((int)response.StatusCode, "INVALID_TOKEN_RESPONSE",
                    "PayPal did not return an access token.", null);
            }

            _accessToken = token.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn - 60, 30));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}

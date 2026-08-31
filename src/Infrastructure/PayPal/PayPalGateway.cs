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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Hand-written PayPal client built to the OpenAPI specifications in
/// api-specs/paypal. Every method maps to one spec operation; paths, field
/// names, auth scheme (OAuth2 client credentials, tokenUrl /v1/oauth2/token)
/// and error model all come from those documents.
///
/// Full card numbers pass through here only inside request bodies; they are
/// never logged and never stored.
/// </summary>
public class PayPalGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly string _baseUrl;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalGateway(HttpClient httpClient, IOptions<PayPalSettings> settings, ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set the PayPal:ClientId and PayPal:ClientSecret " +
                "configuration keys (from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables, " +
                "e.g. via .NET user-secrets).");
        }

        _baseUrl = _settings.ResolveBaseUrl();
    }

    // ---- checkout_orders_v2 ----

    public Task<GatewayAuthorizationResult> AuthorizeWithCardAsync(CardDetails card, decimal amount, string currency,
        string requestId, string? customId, CancellationToken cancellationToken = default)
    {
        var request = BuildAuthorizeRequest(amount, currency, customId);
        request.PaymentSource = new PayPalPaymentSourceRequest
        {
            Card = new PayPalCardRequest
            {
                Name = card.Name,
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                BillingAddress = MapAddress(card.BillingAddress)
            }
        };
        return CreateOrderWithAuthorizationAsync(request, requestId, cancellationToken);
    }

    public Task<GatewayAuthorizationResult> AuthorizeWithVaultedCardAsync(string vaultTokenId, decimal amount,
        string currency, string requestId, string? customId, CancellationToken cancellationToken = default)
    {
        var request = BuildAuthorizeRequest(amount, currency, customId);
        request.PaymentSource = new PayPalPaymentSourceRequest
        {
            Card = new PayPalCardRequest
            {
                VaultId = vaultTokenId,
                StoredCredential = new PayPalStoredCredential
                {
                    PaymentInitiator = "CUSTOMER",
                    PaymentType = "UNSCHEDULED",
                    Usage = "SUBSEQUENT"
                }
            }
        };
        return CreateOrderWithAuthorizationAsync(request, requestId, cancellationToken);
    }

    private static PayPalOrderRequest BuildAuthorizeRequest(decimal amount, string currency, string? customId) =>
        new()
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequest>
            {
                new()
                {
                    ReferenceId = "default",
                    CustomId = customId,
                    Amount = new PayPalMoney(currency, FormatMoney(amount))
                }
            }
        };

    private async Task<GatewayAuthorizationResult> CreateOrderWithAuthorizationAsync(
        PayPalOrderRequest request, string requestId, CancellationToken cancellationToken)
    {
        // Prefer: return=representation so the response carries the authorization
        // resource, per the spec's Prefer header semantics.
        var order = await SendAsync<PayPalOrderResponse>(HttpMethod.Post, "/v2/checkout/orders",
            request, requestId, preferRepresentation: true, cancellationToken);

        var authorization = order.PurchaseUnits?.SelectMany(u => u.Payments?.Authorizations
            ?? Enumerable.Empty<PayPalAuthorization>()).FirstOrDefault();

        return new GatewayAuthorizationResult(
            order.Id ?? string.Empty,
            order.Status ?? string.Empty,
            authorization?.Id,
            authorization?.Status,
            ParseDecimal(authorization?.Amount?.Value),
            authorization?.Amount?.CurrencyCode,
            ParseDate(authorization?.ExpirationTime));
    }

    // ---- payments_payment_v2 ----

    public async Task<GatewayAuthorization> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var auth = await SendAsync<PayPalAuthorization>(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}",
            body: null, requestId: null, preferRepresentation: false, cancellationToken);
        return MapAuthorization(auth);
    }

    public async Task<GatewayCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var capture = await SendAsync<PayPalCapture>(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new PayPalCaptureRequest
            {
                Amount = new PayPalMoney(currency, FormatMoney(amount)),
                FinalCapture = true
            },
            requestId, preferRepresentation: true, cancellationToken);

        return new GatewayCaptureResult(
            capture.Id ?? string.Empty,
            capture.Status ?? string.Empty,
            ParseDecimal(capture.SellerReceivableBreakdown?.GrossAmount?.Value)
                ?? ParseDecimal(capture.Amount?.Value) ?? amount,
            capture.Amount?.CurrencyCode ?? currency,
            ParseDecimal(capture.SellerReceivableBreakdown?.PayPalFee?.Value),
            ParseDecimal(capture.SellerReceivableBreakdown?.NetAmount?.Value));
    }

    public async Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default)
    {
        var auth = await SendAsync<PayPalAuthorization>(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new PayPalReauthorizeRequest { Amount = new PayPalMoney(currency, FormatMoney(amount)) },
            requestId, preferRepresentation: true, cancellationToken);
        return MapAuthorization(auth);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken = default)
    {
        await SendAsync<PayPalAuthorization>(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            body: null, requestId, preferRepresentation: true, cancellationToken);
    }

    public async Task<GatewayRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string requestId, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        // Spec: an empty request body means a full refund; an amount object means partial.
        var body = amount is null
            ? new PayPalRefundRequest { NoteToPayer = noteToPayer }
            : new PayPalRefundRequest
            {
                Amount = new PayPalMoney(currency, FormatMoney(amount.Value)),
                NoteToPayer = noteToPayer
            };

        var refund = await SendAsync<PayPalRefund>(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            body, requestId, preferRepresentation: true, cancellationToken);

        return new GatewayRefundResult(
            refund.Id ?? string.Empty,
            refund.Status ?? string.Empty,
            ParseDecimal(refund.Amount?.Value),
            refund.Amount?.CurrencyCode);
    }

    // ---- vault_payment_tokens_v3 ----

    public async Task<GatewayVaultedCard> SaveCardAsync(string customerId, CardDetails card, string requestId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<PayPalVaultTokenResponse>(HttpMethod.Post, "/v3/vault/payment-tokens",
            new PayPalVaultTokenRequest
            {
                Customer = new PayPalVaultCustomer { Id = customerId },
                PaymentSource = new PayPalVaultPaymentSource
                {
                    Card = new PayPalVaultCard
                    {
                        Name = card.Name,
                        Number = card.Number,
                        Expiry = card.Expiry,
                        SecurityCode = card.SecurityCode,
                        BillingAddress = MapAddress(card.BillingAddress)
                    }
                }
            },
            requestId, preferRepresentation: false, cancellationToken);

        return MapVaultedCard(response);
    }

    public async Task<IReadOnlyList<GatewayVaultedCard>> ListSavedCardsAsync(string customerId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<PayPalVaultTokenListResponse>(HttpMethod.Get,
            $"/v3/vault/payment-tokens?customer_id={Uri.EscapeDataString(customerId)}",
            body: null, requestId: null, preferRepresentation: false, cancellationToken);

        return (response.PaymentTokens ?? new List<PayPalVaultTokenResponse>())
            .Select(MapVaultedCard)
            .ToList();
    }

    public async Task DeleteSavedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultTokenId)}",
            body: null, requestId: null, preferRepresentation: false, cancellationToken);
    }

    // ---- transaction_search_v1 ----

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var all = new List<GatewayTransaction>();
        const int pageSize = 100;
        var page = 1;
        var totalPages = 1;

        while (page <= totalPages)
        {
            var query = $"start_date={FormatDateTime(from)}&end_date={FormatDateTime(to)}" +
                        $"&fields=transaction_info&page_size={pageSize}&page={page}";
            var response = await SendAsync<PayPalTransactionSearchResponse>(HttpMethod.Get,
                $"/v1/reporting/transactions?{query}",
                body: null, requestId: null, preferRepresentation: false, cancellationToken);

            totalPages = response.TotalPages <= 0 ? 1 : response.TotalPages;
            foreach (var detail in response.TransactionDetails ?? new List<PayPalTransactionDetail>())
            {
                var info = detail.TransactionInfo;
                if (info?.TransactionId is null)
                {
                    continue;
                }
                all.Add(new GatewayTransaction(
                    info.TransactionId,
                    info.PayPalReferenceId,
                    info.TransactionEventCode,
                    info.TransactionStatus,
                    ParseDecimal(info.TransactionAmount?.Value),
                    info.TransactionAmount?.CurrencyCode,
                    ParseDecimal(info.FeeAmount?.Value),
                    ParseDate(info.TransactionInitiationDate),
                    ParseDate(info.TransactionUpdatedDate),
                    info.InvoiceId,
                    info.CustomField));
            }

            page++;
        }

        return all;
    }

    // ---- plumbing ----

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? requestId,
        bool preferRepresentation, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, _baseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ParseError(response.StatusCode, payload, method, path);
        }

        if (string.IsNullOrWhiteSpace(payload) || typeof(T) == typeof(object))
        {
            return default!;
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOptions)!;
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

            // OAuth2 clientCredentials flow, tokenUrl /v1/oauth2/token per the specs'
            // security scheme; HTTP Basic with the client credentials.
            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8,
                "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw ParseError(response.StatusCode, payload, HttpMethod.Post, "/v1/oauth2/token");
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(payload, JsonOptions);
            if (string.IsNullOrEmpty(token?.AccessToken))
            {
                throw new PayPalApiException((int)response.StatusCode, null,
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

    private PayPalApiException ParseError(HttpStatusCode statusCode, string payload, HttpMethod method, string path)
    {
        string? name = null;
        string? message = null;
        string? debugId = null;
        var issues = new List<string>();

        try
        {
            var error = JsonSerializer.Deserialize<PayPalErrorResponse>(payload, JsonOptions);
            name = error?.Name;
            message = error?.Message;
            debugId = error?.DebugId;
            foreach (var detail in error?.Details ?? new List<PayPalErrorDetail>())
            {
                if (!string.IsNullOrEmpty(detail.Issue))
                {
                    issues.Add(string.IsNullOrEmpty(detail.Description)
                        ? detail.Issue
                        : $"{detail.Issue}: {detail.Description}");
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through to the generic message.
        }

        var fullMessage = $"{name ?? "PAYPAL_ERROR"}: {message ?? "PayPal request failed."}" +
                          (issues.Count > 0 ? $" ({string.Join("; ", issues)})" : string.Empty) +
                          (debugId is not null ? $" [debug_id {debugId}]" : string.Empty);

        // Never log payloads here: request bodies can contain card data.
        _logger.LogWarning("PayPal {Method} {Path} failed with {StatusCode} {Name} (debug_id {DebugId}).",
            method, path, (int)statusCode, name, debugId);

        return new PayPalApiException((int)statusCode, name, fullMessage, issues);
    }

    private static GatewayAuthorization MapAuthorization(PayPalAuthorization auth) =>
        new(auth.Id ?? string.Empty,
            auth.Status ?? string.Empty,
            ParseDecimal(auth.Amount?.Value),
            auth.Amount?.CurrencyCode,
            ParseDate(auth.ExpirationTime));

    private static GatewayVaultedCard MapVaultedCard(PayPalVaultTokenResponse response) =>
        new(response.Id ?? string.Empty,
            response.PaymentSource?.Card?.Brand,
            response.PaymentSource?.Card?.LastDigits,
            response.PaymentSource?.Card?.Expiry,
            response.PaymentSource?.Card?.Name);

    private static PayPalAddress? MapAddress(CardBillingAddress? address) =>
        address is null
            ? null
            : new PayPalAddress
            {
                AddressLine1 = address.AddressLine1,
                AddressLine2 = address.AddressLine2,
                AdminArea2 = address.AdminArea2,
                AdminArea1 = address.AdminArea1,
                PostalCode = address.PostalCode,
                CountryCode = address.CountryCode
            };

    private static string FormatMoney(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatDateTime(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
}

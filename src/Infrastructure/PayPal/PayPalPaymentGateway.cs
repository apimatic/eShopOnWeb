using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal REST implementation of <see cref="IPaymentGateway"/>, built from the Payments API
/// documentation: OAuth2 client-credentials tokens, Orders v2 (authorize), Payments v2
/// (capture/reauthorize/void/refund), Vault v3 (payment tokens) and Transaction Search v1.
/// Card details are only ever serialized into outgoing request bodies — never logged.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
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

    public async Task<AuthorizationResult> AuthorizeOrderAsync(AuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        var createOrder = new PayPalCreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequest>
            {
                new()
                {
                    Amount = Money(request.Amount, request.Currency),
                    CustomId = request.CustomId,
                    InvoiceId = request.InvoiceId
                }
            },
            PaymentSource = BuildPaymentSource(request)
        };

        // Single-step create (payment source included) requires the idempotency key.
        var order = await SendAsync<PayPalOrderResponse>(HttpMethod.Post, "/v2/checkout/orders",
            createOrder, request.IdempotencyKey, cancellationToken);

        if (RequiresPayerAction(order))
        {
            throw new PayerActionRequiredException(
                "PayPal requires the shopper to approve this payment in a browser (e.g. a 3-D Secure challenge), " +
                "which this server-to-server integration does not support.");
        }

        // A single-step create (payment source included) authorizes immediately; only call
        // the authorize endpoint when the create response carries no authorization yet.
        var authorization = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (authorization is null)
        {
            var authorized = await SendAsync<PayPalOrderResponse>(HttpMethod.Post,
                $"/v2/checkout/orders/{order.Id}/authorize", body: null, requestId: null, cancellationToken);

            if (RequiresPayerAction(authorized))
            {
                throw new PayerActionRequiredException(
                    "PayPal requires the shopper to approve this payment in a browser (e.g. a 3-D Secure challenge), " +
                    "which this server-to-server integration does not support.");
            }

            authorization = authorized.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        }

        if (authorization is null)
        {
            throw new PaymentGatewayException(HttpStatusCode.BadGateway, null,
                "PayPal authorized the order but returned no authorization resource.", null);
        }

        return new AuthorizationResult(
            order.Id!,
            authorization.Id!,
            authorization.Status ?? "UNKNOWN",
            ParseMoney(authorization.Amount),
            authorization.Amount?.CurrencyCode ?? request.Currency,
            ParseTimestamp(authorization.ExpirationTime));
    }

    public async Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var authorization = await SendAsync<PayPalAuthorization>(HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}", body: null, requestId: null, cancellationToken);

        return new AuthorizationDetails(
            authorization.Id!,
            authorization.Status ?? "UNKNOWN",
            ParseMoney(authorization.Amount),
            authorization.Amount?.CurrencyCode ?? string.Empty,
            ParseTimestamp(authorization.ExpirationTime));
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var capture = await SendAsync<PayPalCapture>(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            new PayPalCaptureRequest
            {
                Amount = Money(amount, currency),
                InvoiceId = invoiceId,
                FinalCapture = true
            },
            idempotencyKey, cancellationToken);

        return new CaptureResult(
            capture.Id!,
            capture.Status ?? "UNKNOWN",
            ParseMoney(capture.Amount),
            ParseMoneyOrNull(capture.SellerReceivableBreakdown?.PayPalFee),
            ParseMoneyOrNull(capture.SellerReceivableBreakdown?.NetAmount),
            capture.Amount?.CurrencyCode ?? currency);
    }

    public async Task<AuthorizationDetails> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var authorization = await SendAsync<PayPalAuthorization>(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            new PayPalReauthorizeRequest { Amount = Money(amount, currency) },
            idempotencyKey, cancellationToken);

        return new AuthorizationDetails(
            authorization.Id!,
            authorization.Status ?? "UNKNOWN",
            ParseMoney(authorization.Amount),
            authorization.Amount?.CurrencyCode ?? currency,
            ParseTimestamp(authorization.ExpirationTime));
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await SendAsync<PayPalAuthorization>(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void",
            body: null, idempotencyKey, cancellationToken);
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        var refund = await SendAsync<PayPalRefund>(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            new PayPalRefundRequest
            {
                Amount = amount.HasValue ? Money(amount.Value, currency) : null,
                NoteToPayer = noteToPayer
            },
            idempotencyKey, cancellationToken);

        return new RefundResult(
            refund.Id!,
            refund.Status ?? "UNKNOWN",
            ParseMoney(refund.Amount),
            refund.Amount?.CurrencyCode ?? currency);
    }

    public async Task<VaultedCardResult> VaultCardAsync(string customerId, CardDetails card, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var token = await SendAsync<PayPalPaymentTokenResponse>(HttpMethod.Post, "/v3/vault/payment-tokens",
            new PayPalCreatePaymentTokenRequest
            {
                PaymentSource = new PayPalPaymentSource { Card = MapCard(card) },
                Customer = new PayPalCustomer { Id = ToPayPalCustomerId(customerId) }
            },
            idempotencyKey, cancellationToken);

        return new VaultedCardResult(
            token.Id!,
            token.PaymentSource?.Card?.Brand,
            token.PaymentSource?.Card?.LastDigits,
            token.PaymentSource?.Card?.Expiry,
            token.PaymentSource?.Card?.Name);
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultTokenId}",
            body: null, requestId: null, cancellationToken);
    }

    public async Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<GatewayTransaction>();

        // Transaction Search supports a maximum 31-day window per request: chunk the range.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(30) < to ? windowStart.AddDays(30) : to;

            const int pageSize = 100;
            var page = 1;
            while (true)
            {
                var query = $"/v1/reporting/transactions?start_date={FormatTimestamp(windowStart)}" +
                            $"&end_date={FormatTimestamp(windowEnd)}&fields=all&balance_affecting_records_only=N" +
                            $"&page_size={pageSize}&page={page}&total_required=true";
                var batch = await SendAsync<PayPalTransactionSearchResponse>(HttpMethod.Get, query,
                    body: null, requestId: null, cancellationToken);

                var details = batch.TransactionDetails ?? new List<PayPalTransactionDetail>();
                results.AddRange(details.Select(MapTransaction));

                var more = batch.TotalPages.HasValue
                    ? page < batch.TotalPages.Value
                    : details.Count == pageSize;
                if (!more || details.Count == 0)
                {
                    break;
                }
                page++;
            }

            windowStart = windowEnd;
        }

        return results;
    }

    private static GatewayTransaction MapTransaction(PayPalTransactionDetail detail)
    {
        var info = detail.TransactionInfo ?? new PayPalTransactionInfo();
        return new GatewayTransaction(
            info.TransactionId ?? string.Empty,
            info.TransactionEventCode,
            info.TransactionStatus,
            ParseMoneyOrNull(info.TransactionAmount),
            ParseMoneyOrNull(info.FeeAmount),
            info.TransactionAmount?.CurrencyCode,
            ParseTimestamp(info.TransactionInitiationDate),
            info.InvoiceId,
            info.CustomField);
    }

    // PayPal's customer.id rejects free-form strings such as email addresses, so the
    // shopper identity is mapped to a deterministic, schema-safe id.
    private static string ToPayPalCustomerId(string buyerId)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(buyerId.Trim().ToLowerInvariant()));
        // PayPal customer.id: max 22 chars, [0-9a-zA-Z_-] only.
        return "eshop-" + Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }

    private static PayPalPaymentSource BuildPaymentSource(AuthorizationRequest request)
    {
        if (request.VaultTokenId is not null)
        {
            return new PayPalPaymentSource
            {
                Card = new PayPalCardRequest { VaultId = request.VaultTokenId }
            };
        }
        return new PayPalPaymentSource { Card = MapCard(request.Card!) };
    }

    private static PayPalCardRequest MapCard(CardDetails card)
    {
        return new PayPalCardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = card.BillingAddress is null ? null : new PayPalAddress
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

    private static bool RequiresPayerAction(PayPalOrderResponse order)
    {
        return string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || order.Links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static PayPalMoney Money(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = amount.ToString("F2", CultureInfo.InvariantCulture)
    };

    private static decimal ParseMoney(PayPalMoney? money) =>
        money?.Value is null ? 0m : decimal.Parse(money.Value, CultureInfo.InvariantCulture);

    private static decimal? ParseMoneyOrNull(PayPalMoney? money) =>
        money?.Value is null ? null : decimal.Parse(money.Value, CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, body, requestId, cancellationToken);
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken)
    {
        var response = await SendOnceAsync(method, path, body, requestId, cancellationToken);

        // The access token may have expired between cache check and use: refresh once and retry.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            await RefreshTokenAsync(force: true, cancellationToken);
            response = await SendOnceAsync(method, path, body, requestId, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await ToGatewayExceptionAsync(response, cancellationToken);
        }
        return response;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (requestId is not null)
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (method == HttpMethod.Post)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }
        else if (method == HttpMethod.Post)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<PaymentGatewayException> ToGatewayExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        PayPalErrorResponse? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<PayPalErrorResponse>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through to the generic message below.
        }

        var issues = error?.Details is { Count: > 0 }
            ? " Issues: " + string.Join("; ", error.Details.Select(d => $"{d.Issue} ({d.Field}): {d.Description}"))
            : string.Empty;
        var message = $"PayPal rejected the {(int)response.StatusCode} {response.StatusCode} request: " +
                      $"{error?.Message ?? response.ReasonPhrase}.{issues}";

        // debug_id is PayPal's correlation id for support; safe and useful to log.
        _logger.LogWarning("PayPal call failed with {StatusCode} {ErrorName}; debug_id {DebugId}",
            (int)response.StatusCode, error?.Name, error?.DebugId);

        response.Dispose();
        return new PaymentGatewayException(response.StatusCode, error?.Name, message, error?.DebugId);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }
        return await RefreshTokenAsync(force: false, cancellationToken);
    }

    private async Task<string> RefreshTokenAsync(bool force, CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!force && _accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PayPal token request failed with {StatusCode}", (int)response.StatusCode);
                throw new PaymentGatewayException(response.StatusCode, null,
                    "PayPal rejected the client credentials; verify PayPal:ClientId and PayPal:ClientSecret.", null);
            }

            var token = await ReadJsonAsync<PayPalTokenResponse>(response, cancellationToken);
            _accessToken = token.AccessToken
                ?? throw new PaymentGatewayException(HttpStatusCode.BadGateway, null,
                    "PayPal returned an empty access token.", null);
            // Renew a minute early to avoid using a token at the very edge of expiry.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn - 60, 30));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default!;
        }
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? default!;
    }
}

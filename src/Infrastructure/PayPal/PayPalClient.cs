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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// A hand-written PayPal REST client built strictly against the PayPal OpenAPI specs in
/// <c>api-specs/paypal</c> (Orders v2, Payments v2, Vault v3, Transaction Search v1). No third-party
/// PayPal SDK is used. Endpoints, request/response shapes, auth scheme and error model all come from
/// the spec. Card details flow through this client but are never logged.
/// </summary>
public class PayPalClient : IPayPalPaymentGateway, IPayPalVault, IPayPalReconciliation
{
    public const string HttpClientName = "PayPal";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // PayPal Transaction Search allows at most a 31-day window per request.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);
    private const int SearchPageSize = 500;

    private readonly HttpClient _httpClient;
    private readonly PayPalTokenProvider _tokenProvider;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalClient> _logger;

    public PayPalClient(
        IHttpClientFactory httpClientFactory,
        PayPalTokenProvider tokenProvider,
        IOptions<PayPalOptions> options,
        ILogger<PayPalClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        _tokenProvider = tokenProvider;
        _options = options.Value;
        _logger = logger;
    }

    // ---------------------------------------------------------------- Orders v2 / Payments v2

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizeCardRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var card = request.VaultId is not null
            ? new PpCardSource { VaultId = request.VaultId }
            : ToCardSource(request.Card!);

        var body = new PpOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new()
            {
                new PpPurchaseUnitRequest
                {
                    InvoiceId = request.OrderReference,
                    CustomId = request.OrderReference,
                    Amount = new PpMoney { CurrencyCode = request.Currency, Value = PayPalOptions.FormatMoney(request.Amount, request.Currency) }
                }
            },
            PaymentSource = new PpPaymentSource { Card = card }
        };

        var order = await SendAsync<PpOrder>(HttpMethod.Post, "/v2/checkout/orders", body, idempotencyKey, cancellationToken);
        EnsureNoChallenge(order);

        var authorization = FindAuthorization(order);
        if (authorization is null)
        {
            // Payment source supplied at creation normally advances the order inline; if PayPal left it
            // APPROVED, explicitly create the authorization.
            if (order!.Id is not null && IsAuthorizable(order.Status))
            {
                var authorized = await SendAsync<PpOrder>(HttpMethod.Post, $"/v2/checkout/orders/{order.Id}/authorize", new { }, idempotencyKey + "-auth", cancellationToken);
                EnsureNoChallenge(authorized);
                authorization = FindAuthorization(authorized);
                order = authorized;
            }
        }

        if (authorization?.Id is null || order?.Id is null)
        {
            throw new PayPalApiException($"PayPal did not return an authorization for order (status {order?.Status}).", (int)HttpStatusCode.BadGateway);
        }

        return new AuthorizationResult(order.Id!, authorization.Id!, authorization.Status ?? "CREATED", ParseDate(authorization.ExpirationTime));
    }

    public async Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var auth = await SendAsync<PpAuthorization>(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        return new AuthorizationResult(string.Empty, auth!.Id ?? authorizationId, auth.Status ?? "UNKNOWN", ParseDate(auth.ExpirationTime));
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new PpAmountRequest { Amount = new PpMoney { CurrencyCode = currency, Value = PayPalOptions.FormatMoney(amount, currency) } };
        var auth = await SendAsync<PpAuthorization>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, idempotencyKey, cancellationToken);
        return new AuthorizationResult(string.Empty, auth!.Id ?? authorizationId, auth.Status ?? "CREATED", ParseDate(auth.ExpirationTime));
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, string invoiceReference, CancellationToken cancellationToken = default)
    {
        var body = new PpCaptureRequest
        {
            Amount = new PpMoney { CurrencyCode = currency, Value = PayPalOptions.FormatMoney(amount, currency) },
            FinalCapture = true,
            InvoiceId = invoiceReference
        };
        var capture = await SendAsync<PpCapture>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, idempotencyKey, cancellationToken);

        var breakdown = capture!.SellerReceivableBreakdown;
        var gross = PayPalOptions.ParseMoney(breakdown?.GrossAmount?.Value ?? capture.Amount?.Value) ?? amount;
        var fee = PayPalOptions.ParseMoney(breakdown?.PayPalFee?.Value);
        var net = PayPalOptions.ParseMoney(breakdown?.NetAmount?.Value);
        var currencyCode = breakdown?.GrossAmount?.CurrencyCode ?? capture.Amount?.CurrencyCode ?? currency;

        return new CaptureResult(capture.Id!, capture.Status ?? "COMPLETED", gross, currencyCode, fee, net);
    }

    public async Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, null, cancellationToken, allowEmptyResponse: true);
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, string invoiceReference, CancellationToken cancellationToken = default)
    {
        var body = new PpRefundRequest
        {
            Amount = amount.HasValue ? new PpMoney { CurrencyCode = currency, Value = PayPalOptions.FormatMoney(amount.Value, currency) } : null,
            InvoiceId = invoiceReference
        };
        var refund = await SendAsync<PpRefund>(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, idempotencyKey, cancellationToken);
        var refundedAmount = PayPalOptions.ParseMoney(refund!.Amount?.Value) ?? amount ?? 0m;
        var refundCurrency = refund.Amount?.CurrencyCode ?? currency;
        return new RefundResult(refund.Id!, refund.Status ?? "COMPLETED", refundedAmount, refundCurrency);
    }

    // ---------------------------------------------------------------- Vault v3

    public async Task<SavedCardResult> VaultCardAsync(string customerId, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new PpPaymentTokenRequest
        {
            Customer = new PpCustomer { Id = customerId },
            PaymentSource = new PpVaultPaymentSource { Card = ToCardSource(card) }
        };
        var token = await SendAsync<PpPaymentTokenResponse>(HttpMethod.Post, "/v3/vault/payment-tokens", body, idempotencyKey, cancellationToken);
        var responseCard = token!.PaymentSource?.Card;
        return new SavedCardResult(
            token.Id!,
            token.Customer?.Id ?? customerId,
            responseCard?.Brand,
            responseCard?.LastDigits,
            responseCard?.Expiry,
            responseCard?.Name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, cancellationToken, allowEmptyResponse: true);
    }

    // ---------------------------------------------------------------- Transaction Search v1

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransactionRecord>();

        // Cover the whole range by chunking into PayPal's allowed 31-day windows, and page through each.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxSearchWindow;
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            int totalPages;
            do
            {
                var query = "?" + string.Join("&", new[]
                {
                    "start_date=" + Uri.EscapeDataString(FormatSearchDate(windowStart)),
                    "end_date=" + Uri.EscapeDataString(FormatSearchDate(windowEnd)),
                    "fields=transaction_info",
                    "page_size=" + SearchPageSize.ToString(CultureInfo.InvariantCulture),
                    "page=" + page.ToString(CultureInfo.InvariantCulture),
                });

                var response = await SendAsync<PpSearchResponse>(HttpMethod.Get, "/v1/reporting/transactions" + query, null, null, cancellationToken);
                totalPages = response?.TotalPages ?? 1;

                foreach (var detail in response?.TransactionDetails ?? new List<PpTransactionDetail>())
                {
                    var info = detail.TransactionInfo;
                    if (info is null) continue;
                    results.Add(new PayPalTransactionRecord(
                        info.TransactionId,
                        string.IsNullOrWhiteSpace(info.InvoiceId) ? null : info.InvoiceId,
                        info.CustomField,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        PayPalOptions.ParseMoney(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode,
                        PayPalOptions.ParseMoney(info.FeeAmount?.Value),
                        ParseDate(info.TransactionInitiationDate),
                        ParseDate(info.TransactionUpdatedDate)));
                }

                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd == to ? to : windowEnd;
        }

        return results;
    }

    // ---------------------------------------------------------------- HTTP plumbing

    private async Task<T?> SendAsync<T>(HttpMethod method, string pathAndQuery, object? body, string? requestId, CancellationToken cancellationToken, bool allowEmptyResponse = false)
    {
        var response = await SendCoreAsync(method, pathAndQuery, body, requestId, cancellationToken);

        // A stale token (401) is retried once with a fresh token.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            _tokenProvider.Invalidate();
            response = await SendCoreAsync(method, pathAndQuery, body, requestId, cancellationToken);
        }

        using (response)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("PayPal {Method} {Path} -> {Status}", method, PathOnly(pathAndQuery), (int)response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                throw BuildApiException(response.StatusCode, content, pathAndQuery);
            }

            if (allowEmptyResponse && string.IsNullOrWhiteSpace(content))
            {
                return default;
            }
            if (string.IsNullOrWhiteSpace(content))
            {
                return default;
            }
            return JsonSerializer.Deserialize<T>(content, JsonOptions);
        }
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string pathAndQuery, object? body, string? requestId, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        var request = new HttpRequestMessage(method, pathAndQuery);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.Add("PayPal-Request-Id", requestId);
        }
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, body.GetType(), JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private static Exception BuildApiException(HttpStatusCode statusCode, string content, string pathAndQuery)
    {
        PpError? error = null;
        try { error = string.IsNullOrWhiteSpace(content) ? null : JsonSerializer.Deserialize<PpError>(content); }
        catch { /* non-JSON error body */ }

        var issues = error?.Details is { Count: > 0 }
            ? string.Join("; ", error.Details.Select(d => $"{d.Issue}{(d.Description is not null ? $" ({d.Description})" : string.Empty)}"))
            : null;

        var message = $"PayPal call {PathOnly(pathAndQuery)} failed ({(int)statusCode} {statusCode})"
            + (error?.Name is not null ? $": {error.Name}" : string.Empty)
            + (error?.Message is not null ? $" - {error.Message}" : string.Empty)
            + (issues is not null ? $" [{issues}]" : string.Empty);

        return new PayPalApiException(message, (int)statusCode, error?.Name, error?.DebugId);
    }

    // A card payment PayPal cannot complete without the shopper approving in a browser (3-D Secure
    // step-up etc.). Per the integration contract we STOP rather than build an approval round-trip.
    private static void EnsureNoChallenge(PpOrder? order)
    {
        if (order is null) return;
        var needsPayerAction = string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || (order.Links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(l.Rel, "approve", StringComparison.OrdinalIgnoreCase)) ?? false);
        if (needsPayerAction)
        {
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (e.g. a 3-D Secure challenge). " +
                "This integration does not build an approval round-trip; use a card that authorizes without a challenge.");
        }
    }

    private static PpAuthorization? FindAuthorization(PpOrder? order) =>
        order?.PurchaseUnits?
            .Select(pu => pu.Payments?.Authorizations?.FirstOrDefault(a => a?.Id is not null))
            .FirstOrDefault(a => a is not null);

    private static bool IsAuthorizable(string? status) =>
        string.Equals(status, "APPROVED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "CREATED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "SAVED", StringComparison.OrdinalIgnoreCase);

    private static PpCardSource ToCardSource(CardDetails card) => new()
    {
        Name = card.Name,
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = new PpBillingAddress
        {
            AddressLine1 = card.BillingAddress.AddressLine1,
            AddressLine2 = card.BillingAddress.AddressLine2,
            AdminArea2 = card.BillingAddress.AdminArea2,
            AdminArea1 = card.BillingAddress.AdminArea1,
            PostalCode = card.BillingAddress.PostalCode,
            CountryCode = card.BillingAddress.CountryCode,
        }
    };

    private static DateTimeOffset? ParseDate(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;

    // PayPal date_time pattern requires an offset with a colon (e.g. 2024-01-01T00:00:00+00:00).
    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    private static string PathOnly(string pathAndQuery)
    {
        var q = pathAndQuery.IndexOf('?');
        return q >= 0 ? pathAndQuery[..q] : pathAndQuery;
    }
}

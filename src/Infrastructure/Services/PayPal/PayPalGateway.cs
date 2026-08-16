using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Talks to the PayPal REST v2/v3 APIs: authorize (hold), capture, reauthorize, void, refund,
/// vault cards and list transactions. This is the only place the application calls PayPal.
/// </summary>
public class PayPalGateway : IPaymentGateway
{
    // Per-request date range limit for the Transaction Search API is 31 days; chunk conservatively.
    private static readonly TimeSpan MaxReportWindow = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IPayPalTokenProvider _tokenProvider;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalGateway> _logger;

    public PayPalGateway(HttpClient httpClient, IPayPalTokenProvider tokenProvider,
        PayPalSettings settings, IAppLogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _settings = settings;
        _logger = logger;
    }

    // ---------------------------------------------------------------- Authorize

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizeRequest request, CancellationToken cancellationToken = default)
    {
        var currency = request.CurrencyCode;
        var amountStr = Format(request.Amount);

        object cardSource = request.VaultId is not null
            ? new { vault_id = request.VaultId }
            : BuildCardObject(request.Card!);

        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    invoice_id = request.ReconciliationReference,
                    custom_id = request.ReconciliationReference,
                    amount = new
                    {
                        currency_code = currency,
                        value = amountStr,
                        breakdown = new
                        {
                            item_total = new { currency_code = currency, value = amountStr }
                        }
                    },
                    items = request.Lines.Select(l => new
                    {
                        name = Truncate(l.Name, 127),
                        quantity = l.Quantity.ToString(CultureInfo.InvariantCulture),
                        unit_amount = new { currency_code = currency, value = Format(l.UnitPrice) }
                    }).ToArray()
                }
            },
            payment_source = new { card = cardSource }
        };

        JsonDocument orderDoc;
        try
        {
            orderDoc = await SendAsync(HttpMethod.Post, "v2/checkout/orders", body, request.IdempotencyKey, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode is >= 400 and < 500)
        {
            throw MapCardFailure(ex);
        }

        using (orderDoc)
        {
            var root = orderDoc.RootElement;
            var payPalOrderId = GetString(root, "id") ?? throw new PaymentException("PayPal did not return an order id.");
            var status = GetString(root, "status");

            ThrowIfChallengeRequired(root, status);

            var authorization = ExtractAuthorization(root);
            if (authorization is null && string.Equals(status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                // Payer approved but no authorization yet: explicitly authorize server-side.
                using var authDoc = await SendAsync(HttpMethod.Post, $"v2/checkout/orders/{payPalOrderId}/authorize",
                    new { }, request.IdempotencyKey + "-auth", cancellationToken);
                authorization = ExtractAuthorization(authDoc.RootElement);
            }

            if (authorization is null)
            {
                throw new PaymentException($"PayPal did not return an authorization for the card payment (order status: {status ?? "unknown"}).");
            }

            var (authId, authStatus, expiresAt) = authorization.Value;
            if (authStatus is "DENIED" or "VOIDED" or "EXPIRED")
            {
                throw new PaymentException($"The card payment was not approved (authorization status: {authStatus}).");
            }

            return new AuthorizationResult(payPalOrderId, authId, authStatus, expiresAt);
        }
    }

    // ---------------------------------------------------------------- Capture

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = new { currency_code = currencyCode, value = Format(amount) },
            final_capture = true
        };

        JsonDocument doc;
        try
        {
            doc = await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/capture",
                body, idempotencyKey, cancellationToken);
        }
        catch (PayPalApiException ex) when (IsExpiredAuthorization(ex))
        {
            throw new AuthorizationExpiredException(
                $"PayPal reports the authorization {authorizationId} can no longer be captured directly ({DescribeIssues(ex)}).");
        }

        using (doc)
        {
            var root = doc.RootElement;
            var captureId = GetString(root, "id") ?? throw new PaymentException("PayPal capture response had no id.");
            var status = GetString(root, "status") ?? "UNKNOWN";
            var capturedAt = GetDateTime(root, "create_time");

            decimal gross = amount, fee = 0m, net = amount;
            if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown) && breakdown.ValueKind == JsonValueKind.Object)
            {
                gross = GetMoney(breakdown, "gross_amount") ?? gross;
                fee = GetMoney(breakdown, "paypal_fee") ?? fee;
                net = GetMoney(breakdown, "net_amount") ?? net;
            }

            return new CaptureResult(captureId, status, gross, fee, net, currencyCode, capturedAt);
        }
    }

    // ---------------------------------------------------------------- Reauthorize / Get / Void

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode,
        CancellationToken cancellationToken = default)
    {
        var body = new { amount = new { currency_code = currencyCode, value = Format(amount) } };

        using var doc = await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/reauthorize",
            body, $"reauth-{authorizationId}", cancellationToken);

        var root = doc.RootElement;
        var newAuthId = GetString(root, "id") ?? throw new PaymentException("PayPal reauthorize response had no id.");
        var status = GetString(root, "status") ?? "CREATED";
        var expiresAt = GetDateTime(root, "expiration_time");
        return new AuthorizationResult(string.Empty, newAuthId, status, expiresAt);
    }

    public async Task<AuthorizationResult?> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        JsonDocument doc;
        try
        {
            doc = await SendAsync(HttpMethod.Get, $"v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            var id = GetString(root, "id") ?? authorizationId;
            var status = GetString(root, "status") ?? "UNKNOWN";
            var expiresAt = GetDateTime(root, "expiration_time");
            return new AuthorizationResult(string.Empty, id, status, expiresAt);
        }
    }

    public async Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void",
            null, $"void-{authorizationId}", cancellationToken);
    }

    // ---------------------------------------------------------------- Refund

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, string? invoiceId, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        object? body = null;
        if (amount is not null || invoiceId is not null || noteToPayer is not null)
        {
            body = new
            {
                amount = amount is null ? null : new { currency_code = currencyCode, value = Format(amount.Value) },
                invoice_id = invoiceId,
                note_to_payer = noteToPayer
            };
        }

        // The PayPal-Request-Id must be globally unique per merchant, so scope the caller's
        // idempotency key to this capture. It stays stable for the same key (PayPal-level dedupe),
        // while app-level idempotency already prevents re-issuing a refund for a used key.
        var requestId = $"refund-{captureId}-{idempotencyKey}";

        using var doc = await SendAsync(HttpMethod.Post, $"v2/payments/captures/{captureId}/refund",
            body, requestId, cancellationToken);

        var root = doc.RootElement;
        var refundId = GetString(root, "id") ?? throw new PaymentException("PayPal refund response had no id.");
        var status = GetString(root, "status") ?? "UNKNOWN";
        var refundedAmount = GetMoney(root, "amount") ?? amount ?? 0m;
        return new RefundResult(refundId, status, refundedAmount, currencyCode);
    }

    // ---------------------------------------------------------------- Vault

    public async Task<VaultResult> VaultCardAsync(PaymentCard card, string buyerReference, CancellationToken cancellationToken = default)
    {
        var requestId = $"vault-{Guid.NewGuid():N}";

        // Preferred: create a payment token directly from the card.
        try
        {
            var body = new { payment_source = new { card = BuildCardObject(card) } };
            using var doc = await SendAsync(HttpMethod.Post, "v3/vault/payment-tokens", body, requestId, cancellationToken);
            return ParseVaultResult(doc.RootElement, card);
        }
        catch (PayPalApiException ex) when (ex.StatusCode is >= 400 and < 500)
        {
            _logger.LogWarning($"Direct vault payment-token failed ({DescribeIssues(ex)}); falling back to setup-token flow.");
        }

        // Fallback: setup token -> payment token exchange.
        var setupBody = new { payment_source = new { card = BuildCardObject(card) } };
        string setupTokenId;
        using (var setupDoc = await SendAsync(HttpMethod.Post, "v3/vault/setup-tokens", setupBody,
            $"{requestId}-setup", cancellationToken))
        {
            setupTokenId = GetString(setupDoc.RootElement, "id")
                ?? throw new PaymentException("PayPal setup-token response had no id.");
        }

        var exchangeBody = new { payment_source = new { token = new { id = setupTokenId, type = "SETUP_TOKEN" } } };
        using var tokenDoc = await SendAsync(HttpMethod.Post, "v3/vault/payment-tokens", exchangeBody,
            $"{requestId}-token", cancellationToken);
        return ParseVaultResult(tokenDoc.RootElement, card);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var doc = await SendAsync(HttpMethod.Delete, $"v3/vault/payment-tokens/{vaultId}", null, null, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            // Already gone at PayPal — nothing more to do.
        }
    }

    // ---------------------------------------------------------------- Reconciliation

    public async Task<IReadOnlyList<TransactionRecord>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TransactionRecord>();

        // Chunk the (possibly long) range into windows within PayPal's 31-day per-request limit.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxReportWindow;
            if (windowEnd > to) windowEnd = to;

            await CollectWindowAsync(windowStart, windowEnd, results, cancellationToken);

            windowStart = windowEnd;
        }

        return results;
    }

    private async Task CollectWindowAsync(DateTimeOffset start, DateTimeOffset end, List<TransactionRecord> sink,
        CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        var page = 1;
        var totalPages = 1;

        do
        {
            var query =
                $"v1/reporting/transactions?start_date={EscapeDate(start)}&end_date={EscapeDate(end)}" +
                $"&fields=transaction_info&page_size={pageSize}&page={page}";

            using var doc = await SendAsync(HttpMethod.Get, query, null, null, cancellationToken);
            var root = doc.RootElement;

            if (root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var pages))
            {
                totalPages = Math.Max(1, pages);
            }

            if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (!detail.TryGetProperty("transaction_info", out var info)) continue;

                    var record = new TransactionRecord(
                        TransactionId: GetString(info, "transaction_id") ?? string.Empty,
                        Status: GetString(info, "transaction_status") ?? "UNKNOWN",
                        Amount: GetMoney(info, "transaction_amount") ?? 0m,
                        CurrencyCode: GetMoneyCurrency(info, "transaction_amount") ?? _settings.Currency,
                        Fee: GetMoney(info, "fee_amount") ?? 0m,
                        InvoiceId: GetString(info, "invoice_id"),
                        CustomField: GetString(info, "custom_field"),
                        Date: GetDateTime(info, "transaction_initiation_date"),
                        Subject: GetString(info, "transaction_subject"),
                        EventCode: GetString(info, "transaction_event_code"));
                    sink.Add(record);
                }
            }

            page++;
        }
        while (page <= totalPages);
    }

    // ---------------------------------------------------------------- HTTP core

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);

            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
            }
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _tokenProvider.Invalidate();
                continue; // refresh token and retry once
            }

            if (!response.IsSuccessStatusCode)
            {
                throw BuildApiException(response.StatusCode, content);
            }

            // Some successful calls (void, delete) return 204/empty.
            if (string.IsNullOrWhiteSpace(content))
            {
                return JsonDocument.Parse("{}");
            }

            return JsonDocument.Parse(content);
        }
    }

    private PayPalApiException BuildApiException(HttpStatusCode statusCode, string content)
    {
        string? name = null;
        string? message = null;
        string? debugId = null;
        var issues = new List<string>();

        try
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                name = GetString(root, "name");
                message = GetString(root, "message");
                debugId = GetString(root, "debug_id");
                if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d in details.EnumerateArray())
                    {
                        var issue = GetString(d, "issue");
                        if (!string.IsNullOrEmpty(issue)) issues.Add(issue!);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with what we have.
        }

        var summary = $"PayPal API error (HTTP {(int)statusCode})";
        if (!string.IsNullOrEmpty(name)) summary += $" {name}";
        if (issues.Count > 0) summary += $" [{string.Join(", ", issues)}]";
        if (!string.IsNullOrEmpty(message)) summary += $": {message}";
        if (!string.IsNullOrEmpty(debugId)) summary += $" (debug_id: {debugId})";

        _logger.LogWarning(summary);
        return new PayPalApiException((int)statusCode, name, debugId, issues, summary);
    }

    // ---------------------------------------------------------------- Helpers

    private static object BuildCardObject(PaymentCard card)
    {
        object? billing = null;
        if (card.BillingAddress is { } b)
        {
            billing = new
            {
                address_line_1 = b.Line1,
                address_line_2 = b.Line2,
                admin_area_2 = b.City,
                admin_area_1 = b.State,
                postal_code = b.PostalCode,
                country_code = b.CountryCode
            };
        }

        return new
        {
            number = card.Number,
            expiry = $"{card.ExpiryYear:D4}-{card.ExpiryMonth:D2}",
            security_code = card.SecurityCode,
            name = card.CardholderName,
            billing_address = billing
        };
    }

    private static VaultResult ParseVaultResult(JsonElement root, PaymentCard card)
    {
        var vaultId = GetString(root, "id")
            ?? throw new PaymentException("PayPal vault response did not contain a payment-token id.");

        string? brand = null, lastFour = null, expiry = null, name = null;
        if (root.TryGetProperty("payment_source", out var source) &&
            source.TryGetProperty("card", out var cardEl))
        {
            brand = GetString(cardEl, "brand");
            lastFour = GetString(cardEl, "last_digits");
            expiry = GetString(cardEl, "expiry");
            name = GetString(cardEl, "name");
        }

        lastFour ??= card.Number.Length >= 4 ? card.Number[^4..] : null;
        expiry ??= $"{card.ExpiryYear:D4}-{card.ExpiryMonth:D2}";
        return new VaultResult(vaultId, brand, lastFour, expiry, name);
    }

    private static (string Id, string Status, DateTimeOffset? ExpiresAt)? ExtractAuthorization(JsonElement orderRoot)
    {
        if (!orderRoot.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var unit in units.EnumerateArray())
        {
            if (!unit.TryGetProperty("payments", out var payments)) continue;
            if (!payments.TryGetProperty("authorizations", out var auths) || auths.ValueKind != JsonValueKind.Array) continue;

            foreach (var auth in auths.EnumerateArray())
            {
                var id = GetString(auth, "id");
                if (id is null) continue;
                var status = GetString(auth, "status") ?? "CREATED";
                var expiresAt = GetDateTime(auth, "expiration_time");
                return (id, status, expiresAt);
            }
        }

        return null;
    }

    private static void ThrowIfChallengeRequired(JsonElement root, string? status)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (e.g. 3-D Secure). " +
                "This server-to-server integration does not perform a browser approval round-trip.");
        }

        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                if (string.Equals(GetString(link, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    throw new PaymentChallengeRequiredException(
                        "PayPal requires the shopper to approve this card payment in a browser (e.g. 3-D Secure). " +
                        "This server-to-server integration does not perform a browser approval round-trip.");
                }
            }
        }
    }

    private static bool IsExpiredAuthorization(PayPalApiException ex) =>
        ex.HasIssue("AUTHORIZATION_EXPIRED") || ex.HasIssue("EXPIRED_AUTHORIZATION");

    private static PaymentException MapCardFailure(PayPalApiException ex)
    {
        if (ex.HasIssue("INSTRUMENT_DECLINED") || ex.HasIssue("CARD_EXPIRED") ||
            ex.HasIssue("PAYER_CANNOT_PAY") || ex.HasIssue("TRANSACTION_REFUSED"))
        {
            return new PaymentException($"The card was declined by PayPal ({DescribeIssues(ex)}). Try a different card.");
        }
        return new PaymentException($"PayPal could not process the card payment ({DescribeIssues(ex)}).", ex);
    }

    private static string DescribeIssues(PayPalApiException ex) =>
        ex.Issues.Count > 0 ? string.Join(", ", ex.Issues) : (ex.Name ?? $"HTTP {ex.StatusCode}");

    private static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string EscapeDate(DateTimeOffset value) =>
        Uri.EscapeDataString(value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value.Substring(0, max));

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? GetMoney(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var money) && money.ValueKind == JsonValueKind.Object &&
            money.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }
        return null;
    }

    private static string? GetMoneyCurrency(JsonElement element, string property) =>
        element.TryGetProperty(property, out var money) && money.ValueKind == JsonValueKind.Object
            ? GetString(money, "currency_code")
            : null;

    private static DateTimeOffset? GetDateTime(JsonElement element, string property)
    {
        var raw = GetString(element, property);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var result)
            ? result
            : null;
    }
}

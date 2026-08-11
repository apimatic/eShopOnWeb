using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Verified, plain-HTTP client for the PayPal REST APIs used by this integration (Orders v2, Payments v2,
/// Vault v3, Transaction Search v1). Card data flows only outbound to PayPal — it is never persisted or
/// written to logs. The OAuth token is cached until shortly before it expires.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private const string TokenCacheKey = "paypal-access-token";

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalClient> _logger;

    public PayPalClient(HttpClient httpClient, IMemoryCache cache, IOptions<PayPalSettings> settings, IAppLogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _settings = settings.Value;
        _logger = logger;
    }

    // ---------------------------------------------------------------- Orders v2 (authorize hold)

    public async Task<PayPalAuthorizationResult> AuthorizeOrderWithCardAsync(
        decimal amount, string currencyCode, PayPalCardDetails card, string invoiceId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = BuildCreateOrder(amount, currencyCode, invoiceId, new JsonObject { ["card"] = BuildCardNode(card, includeSecurityCode: true) });
        var created = await SendAsync(HttpMethod.Post, "v2/checkout/orders", body, idempotencyKey, "create order", cancellationToken);
        return await FinalizeAuthorizationAsync(created, idempotencyKey, cancellationToken);
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderWithVaultAsync(
        decimal amount, string currencyCode, string vaultId, string invoiceId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = BuildCreateOrder(amount, currencyCode, invoiceId,
            new JsonObject { ["card"] = new JsonObject { ["vault_id"] = vaultId } });
        var created = await SendAsync(HttpMethod.Post, "v2/checkout/orders", body, idempotencyKey, "create order (saved card)", cancellationToken);
        return await FinalizeAuthorizationAsync(created, idempotencyKey, cancellationToken);
    }

    private static JsonObject BuildCreateOrder(decimal amount, string currencyCode, string invoiceId, JsonObject paymentSource) =>
        new()
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray(new JsonObject
            {
                ["invoice_id"] = invoiceId,
                ["custom_id"] = invoiceId,
                ["amount"] = new JsonObject
                {
                    ["currency_code"] = currencyCode,
                    ["value"] = FormatMoney(amount)
                }
            }),
            ["payment_source"] = paymentSource
        };

    private async Task<PayPalAuthorizationResult> FinalizeAuthorizationAsync(JsonNode? created, string idempotencyKey, CancellationToken cancellationToken)
    {
        var payPalOrderId = GetString(created, "id")
            ?? throw new PayPalApiException(502, "NO_ORDER_ID", null, null, "PayPal did not return an order id.");
        var status = GetString(created, "status") ?? "UNKNOWN";

        GuardAgainstChallenge(status);

        var authorization = ExtractAuthorization(created);
        if (authorization is null)
        {
            // The card was supplied at create time but the hold was not placed inline: authorize explicitly.
            var authorized = await SendAsync(HttpMethod.Post, $"v2/checkout/orders/{payPalOrderId}/authorize",
                new JsonObject(), idempotencyKey + "-auth", "authorize order", cancellationToken);
            status = GetString(authorized, "status") ?? status;
            GuardAgainstChallenge(status);
            authorization = ExtractAuthorization(authorized)
                ?? throw new PayPalApiException(502, "NO_AUTHORIZATION", null, null,
                    "PayPal did not return an authorization for the order.");
        }

        var authId = GetString(authorization, "id")
            ?? throw new PayPalApiException(502, "NO_AUTHORIZATION_ID", null, null, "PayPal authorization is missing its id.");
        var authStatus = GetString(authorization, "status") ?? "CREATED";
        return new PayPalAuthorizationResult(payPalOrderId, authId, authStatus, status);
    }

    private static void GuardAgainstChallenge(string orderStatus)
    {
        if (string.Equals(orderStatus, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this card in a browser (3-D Secure). " +
                "This API does not support a browser approval round-trip; ask the shopper to use a different card.");
        }
    }

    private static JsonNode? ExtractAuthorization(JsonNode? order)
    {
        var authorizations = order?["purchase_units"]?[0]?["payments"]?["authorizations"]?.AsArray();
        return authorizations is { Count: > 0 } ? authorizations[0] : null;
    }

    // ---------------------------------------------------------------- Payments v2

    public async Task<string> GetAuthorizationStatusAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var node = await SendAsync(HttpMethod.Get, $"v2/payments/authorizations/{authorizationId}", null, null,
            "get authorization", cancellationToken);
        return GetString(node, "status") ?? "UNKNOWN";
    }

    public async Task<string> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = new JsonObject { ["currency_code"] = currencyCode, ["value"] = FormatMoney(amount) }
        };
        var node = await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/reauthorize", body, null,
            "reauthorize", cancellationToken);
        return GetString(node, "id")
            ?? throw new PayPalApiException(502, "NO_AUTHORIZATION_ID", null, null, "PayPal reauthorization is missing its id.");
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currencyCode,
        string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["final_capture"] = true,
            ["invoice_id"] = invoiceId,
            ["amount"] = new JsonObject { ["currency_code"] = currencyCode, ["value"] = FormatMoney(amount) }
        };
        var node = await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/capture", body, idempotencyKey,
            "capture", cancellationToken);

        var captureId = GetString(node, "id")
            ?? throw new PayPalApiException(502, "NO_CAPTURE_ID", null, null, "PayPal capture is missing its id.");
        var status = GetString(node, "status") ?? "COMPLETED";

        var breakdown = node?["seller_receivable_breakdown"];
        var gross = ParseMoney(breakdown?["gross_amount"]?["value"]) ?? amount;
        var fee = ParseMoney(breakdown?["paypal_fee"]?["value"]) ?? 0m;
        var net = ParseMoney(breakdown?["net_amount"]?["value"]) ?? (gross - fee);
        var currency = GetString(breakdown?["gross_amount"], "currency_code") ?? currencyCode;

        return new PayPalCaptureResult(captureId, status, gross, fee, net, currency);
    }

    public async Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void", null, null, "void", cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode,
        string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["invoice_id"] = invoiceId };
        if (amount.HasValue)
        {
            body["amount"] = new JsonObject { ["currency_code"] = currencyCode, ["value"] = FormatMoney(amount.Value) };
        }

        var node = await SendAsync(HttpMethod.Post, $"v2/payments/captures/{captureId}/refund", body, idempotencyKey,
            "refund", cancellationToken);

        var refundId = GetString(node, "id")
            ?? throw new PayPalApiException(502, "NO_REFUND_ID", null, null, "PayPal refund is missing its id.");
        var status = GetString(node, "status") ?? "COMPLETED";
        var refundedValue = ParseMoney(node?["amount"]?["value"]) ?? amount ?? 0m;
        var currency = GetString(node?["amount"], "currency_code") ?? currencyCode;
        return new PayPalRefundResult(refundId, status, refundedValue, currency);
    }

    // ---------------------------------------------------------------- Vault v3 (saved cards)

    public async Task<(string SetupTokenId, string CustomerId)> CreateSetupTokenAsync(
        PayPalCardDetails card, string? customerId, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["payment_source"] = new JsonObject { ["card"] = BuildCardNode(card, includeSecurityCode: false) }
        };
        if (!string.IsNullOrEmpty(customerId))
        {
            body["customer"] = new JsonObject { ["id"] = customerId };
        }

        var node = await SendAsync(HttpMethod.Post, "v3/vault/setup-tokens", body, Guid.NewGuid().ToString("N"),
            "create setup token", cancellationToken);

        var setupTokenId = GetString(node, "id")
            ?? throw new PayPalApiException(502, "NO_SETUP_TOKEN", null, null, "PayPal did not return a setup token id.");
        var resolvedCustomerId = GetString(node?["customer"], "id") ?? customerId ?? string.Empty;
        return (setupTokenId, resolvedCustomerId);
    }

    public async Task<PayPalVaultedCard> CreatePaymentTokenAsync(string setupTokenId, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["payment_source"] = new JsonObject
            {
                ["token"] = new JsonObject { ["id"] = setupTokenId, ["type"] = "SETUP_TOKEN" }
            }
        };
        var node = await SendAsync(HttpMethod.Post, "v3/vault/payment-tokens", body, Guid.NewGuid().ToString("N"),
            "create payment token", cancellationToken);

        var vaultId = GetString(node, "id")
            ?? throw new PayPalApiException(502, "NO_VAULT_TOKEN", null, null, "PayPal did not return a vault token id.");
        var customerId = GetString(node?["customer"], "id") ?? string.Empty;
        var cardNode = node?["payment_source"]?["card"];
        var brand = GetString(cardNode, "brand") ?? "CARD";
        var lastDigits = GetString(cardNode, "last_digits") ?? "0000";
        var expiry = GetString(cardNode, "expiry") ?? string.Empty;

        return new PayPalVaultedCard(vaultId, customerId, brand, lastDigits, expiry);
    }

    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"v3/vault/payment-tokens/{vaultId}", null, null, "delete payment token", cancellationToken);
    }

    // ---------------------------------------------------------------- Transaction Search v1 (reconciliation)

    public async Task<IReadOnlyList<PayPalTransactionRecord>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransactionRecord>();

        // Transaction reporting cannot look into the future; cap the end at "now".
        var now = DateTimeOffset.UtcNow;
        var effectiveTo = to > now ? now : to;
        if (from >= effectiveTo)
        {
            return results;
        }

        // Cover the WHOLE range by chunking into <=31-day windows (PayPal's maximum).
        var windowStart = from;
        while (windowStart < effectiveTo)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > effectiveTo)
            {
                windowEnd = effectiveTo;
            }

            await FetchTransactionWindowAsync(windowStart, windowEnd, results, cancellationToken);
            windowStart = windowEnd;
        }

        // De-duplicate across window boundaries.
        return results
            .GroupBy(t => t.TransactionId)
            .Select(g => g.First())
            .ToList();
    }

    private async Task FetchTransactionWindowAsync(DateTimeOffset start, DateTimeOffset end,
        List<PayPalTransactionRecord> results, CancellationToken cancellationToken)
    {
        var page = 1;
        int totalPages;
        do
        {
            var query = "v1/reporting/transactions" +
                        $"?start_date={Uri.EscapeDataString(FormatDate(start))}" +
                        $"&end_date={Uri.EscapeDataString(FormatDate(end))}" +
                        "&fields=transaction_info&page_size=500" +
                        $"&page={page}";

            var node = await SendAsync(HttpMethod.Get, query, null, null, "list transactions", cancellationToken);
            totalPages = (int?)(node?["total_pages"]) ?? 0;

            var details = node?["transaction_details"]?.AsArray();
            if (details is not null)
            {
                foreach (var detail in details)
                {
                    var info = detail?["transaction_info"];
                    if (info is null)
                    {
                        continue;
                    }

                    results.Add(new PayPalTransactionRecord(
                        GetString(info, "transaction_id") ?? string.Empty,
                        GetString(info, "transaction_status") ?? string.Empty,
                        ParseMoney(info["transaction_amount"]?["value"]) ?? 0m,
                        GetString(info["transaction_amount"], "currency_code") ?? _settings.Currency,
                        ParseMoney(info["fee_amount"]?["value"]),
                        GetString(info, "invoice_id"),
                        ParseDate(info["transaction_initiation_date"])));
                }
            }

            page++;
        }
        while (page <= totalPages);
    }

    // ---------------------------------------------------------------- HTTP plumbing

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body, string? requestId,
        string operation, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            ThrowPayPalError((int)response.StatusCode, content, operation, response);
        }

        return string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cachedToken) && !string.IsNullOrEmpty(cachedToken))
        {
            return cachedToken;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            ThrowPayPalError((int)response.StatusCode, content, "authenticate", response);
        }

        var node = JsonNode.Parse(content);
        var accessToken = GetString(node, "access_token")
            ?? throw new PayPalApiException(502, "NO_ACCESS_TOKEN", null, null, "PayPal did not return an access token.");
        var expiresIn = (int?)(node?["expires_in"]) ?? 3000;

        _cache.Set(TokenCacheKey, accessToken, TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60)));
        return accessToken;
    }

    private void ThrowPayPalError(int statusCode, string content, string operation, HttpResponseMessage response)
    {
        string? name = null, message = null, debugId = null;
        var issues = new List<string>();

        try
        {
            var node = JsonNode.Parse(content);
            name = GetString(node, "name") ?? GetString(node, "error");
            message = GetString(node, "message") ?? GetString(node, "error_description");
            debugId = GetString(node, "debug_id");
            var details = node?["details"]?.AsArray();
            if (details is not null)
            {
                foreach (var detail in details)
                {
                    var issue = GetString(detail, "issue");
                    if (issue is not null)
                    {
                        issues.Add(issue);
                    }
                }
            }
        }
        catch
        {
            // Non-JSON error body; fall back to the raw content in the message.
        }

        if (debugId is null && response.Headers.TryGetValues("Paypal-Debug-Id", out var debugValues))
        {
            debugId = string.Join(",", debugValues);
        }

        var issueText = issues.Count > 0 ? $" [{string.Join(", ", issues)}]" : string.Empty;
        var detailText = message ?? (string.IsNullOrWhiteSpace(content) ? "(no response body)" : content);
        var errorMessage = $"PayPal {operation} failed ({statusCode} {name}){issueText}: {detailText}";

        _logger.LogWarning($"PayPal {operation} failed ({statusCode} {name}) debug_id={debugId}.");
        throw new PayPalApiException(statusCode, name, issues, debugId, errorMessage);
    }

    private static JsonObject BuildCardNode(PayPalCardDetails card, bool includeSecurityCode)
    {
        var node = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry
        };

        if (includeSecurityCode && !string.IsNullOrEmpty(card.SecurityCode))
        {
            node["security_code"] = card.SecurityCode;
        }
        if (!string.IsNullOrEmpty(card.CardholderName))
        {
            node["name"] = card.CardholderName;
        }

        var billing = new JsonObject();
        if (!string.IsNullOrEmpty(card.AddressLine1)) billing["address_line_1"] = card.AddressLine1;
        if (!string.IsNullOrEmpty(card.AddressLine2)) billing["address_line_2"] = card.AddressLine2;
        if (!string.IsNullOrEmpty(card.AdminArea2)) billing["admin_area_2"] = card.AdminArea2;
        if (!string.IsNullOrEmpty(card.AdminArea1)) billing["admin_area_1"] = card.AdminArea1;
        if (!string.IsNullOrEmpty(card.PostalCode)) billing["postal_code"] = card.PostalCode;
        if (!string.IsNullOrEmpty(card.CountryCode)) billing["country_code"] = card.CountryCode;
        if (billing.Count > 0)
        {
            node["billing_address"] = billing;
        }

        return node;
    }

    private static string FormatMoney(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string? GetString(JsonNode? node, string property)
    {
        if (node is not JsonObject obj || !obj.TryGetPropertyValue(property, out var value) || value is null)
        {
            return null;
        }
        return value.ToString();
    }

    private static decimal? ParseMoney(JsonNode? value)
    {
        var text = value?.ToString();
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static DateTimeOffset? ParseDate(JsonNode? value)
    {
        var text = value?.ToString();
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result) ? result : null;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// The single implementation of <see cref="IPayPalClient"/> over PayPal's REST API. Handles OAuth token
/// caching, idempotency headers, error translation and response parsing. Card data is only ever sent in
/// request bodies to PayPal; it is never logged.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private const string TokenCacheKey = "paypal:access_token";
    private const int MaxTransactionWindowDays = 31;
    private const int TransactionPageSize = 500;

    private static readonly SemaphoreSlim TokenLock = new(1, 1);

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<PayPalClient> _logger;

    public PayPalClient(HttpClient httpClient, PayPalSettings settings, IMemoryCache cache,
        IAppLogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> CreateAuthorizationOrderAsync(decimal amount, string currencyCode, string referenceId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray(new JsonObject
            {
                ["reference_id"] = referenceId,
                // custom_id (not invoice_id) carries the eShop order id: it needs no account-wide uniqueness,
                // so it survives the in-memory store restarting order ids, and it appears in transaction reports.
                ["custom_id"] = referenceId,
                ["amount"] = new JsonObject
                {
                    ["currency_code"] = currencyCode,
                    ["value"] = FormatAmount(amount)
                }
            })
        };

        var response = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, idempotencyKey,
            "return=representation", cancellationToken);
        return response.GetProperty("id").GetString()!;
    }

    public async Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(string payPalOrderId, PayPalCardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["payment_source"] = new JsonObject { ["card"] = BuildCardNode(card, forVault: false) }
        };
        var response = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize", body,
            idempotencyKey, "return=representation", cancellationToken);
        return ParseAuthorization(response, payPalOrderId);
    }

    public async Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(string payPalOrderId, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["payment_source"] = new JsonObject { ["card"] = new JsonObject { ["vault_id"] = vaultId } }
        };
        var response = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize", body,
            idempotencyKey, "return=representation", cancellationToken);
        return ParseAuthorization(response, payPalOrderId);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", new JsonObject(),
            idempotencyKey, "return=representation", cancellationToken);

        var (status, expiresAt) = ReadAuthorizationStatus(response);
        var newId = response.TryGetProperty("id", out var id) ? id.GetString()! : authorizationId;
        return new PayPalAuthorizationResult(string.Empty, newId, status, expiresAt, null, null, false);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["final_capture"] = true };
        var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", body, idempotencyKey,
            "return=representation", cancellationToken);

        var captureId = response.GetProperty("id").GetString()!;
        var status = response.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        var (gross, currency) = ReadMoney(response, "amount");

        decimal? fee = null, net = null;
        if (response.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            var (breakdownGross, _) = ReadMoney(breakdown, "gross_amount");
            if (breakdownGross > 0) gross = breakdownGross;
            fee = ReadOptionalMoney(breakdown, "paypal_fee");
            net = ReadOptionalMoney(breakdown, "net_amount");
        }

        return new PayPalCaptureResult(captureId, status, gross, fee, net, currency);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null,
            null, null, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Empty object = full refund of the remaining amount; an amount object = a partial refund.
        var body = new JsonObject();
        if (amount.HasValue)
        {
            body["amount"] = new JsonObject
            {
                ["currency_code"] = currencyCode,
                ["value"] = FormatAmount(amount.Value)
            };
        }

        var response = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body,
            idempotencyKey, "return=representation", cancellationToken);

        var refundId = response.GetProperty("id").GetString()!;
        var status = response.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        var (refunded, currency) = ReadMoney(response, "amount");
        if (refunded <= 0m && amount.HasValue) refunded = amount.Value;
        if (string.IsNullOrEmpty(currency)) currency = currencyCode;

        return new PayPalRefundResult(refundId, status, refunded, currency);
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(PayPalCardDetails card, string merchantCustomerId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["payment_source"] = new JsonObject { ["card"] = BuildCardNode(card, forVault: true) }
        };
        if (!string.IsNullOrWhiteSpace(merchantCustomerId))
        {
            body["customer"] = new JsonObject { ["merchant_customer_id"] = merchantCustomerId };
        }

        var response = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, idempotencyKey,
            "return=representation", cancellationToken);

        var vaultId = response.GetProperty("id").GetString()!;
        var customerId = merchantCustomerId;
        if (response.TryGetProperty("customer", out var customer) &&
            customer.TryGetProperty("id", out var custId) && custId.GetString() is { } cid)
        {
            customerId = cid;
        }

        string? brand = null, last4 = null, expiry = null, name = null;
        if (response.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            brand = GetString(cardEl, "brand");
            last4 = GetString(cardEl, "last_digits");
            expiry = GetString(cardEl, "expiry");
            name = GetString(cardEl, "name");
        }

        return new PayPalVaultedCard(vaultId, customerId, brand, last4, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();

        // Transaction Search is limited to ~31 days per request, so chunk the whole range into windows and
        // page each window to the end. This covers the whole range, not just the first page of it.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(MaxTransactionWindowDays);
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            int totalPages;
            do
            {
                var path = "/v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(FormatTimestamp(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatTimestamp(windowEnd))}" +
                    $"&fields=all&page_size={TransactionPageSize}&page={page}";

                var response = await SendAsync(HttpMethod.Get, path, null, null, null, cancellationToken);

                if (response.TryGetProperty("transaction_details", out var details) &&
                    details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        results.Add(ParseTransaction(detail));
                    }
                }

                totalPages = response.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var t) ? t : 1;
                page++;
            }
            while (page <= totalPages);

            if (windowEnd >= to) break;
            windowStart = windowEnd;
        }

        return results;
    }

    // --- HTTP plumbing ---

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, JsonObject? body, string? requestId,
        string? prefer, CancellationToken cancellationToken, bool allowTokenRefresh = true)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", Truncate(requestId, 108));
        }
        if (!string.IsNullOrEmpty(prefer))
        {
            request.Headers.TryAddWithoutValidation("Prefer", prefer);
        }
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && allowTokenRefresh)
        {
            _cache.Remove(TokenCacheKey);
            return await SendAsync(method, path, body, requestId, prefer, cancellationToken, allowTokenRefresh: false);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw BuildApiException((int)response.StatusCode, content);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached!;
        }

        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(TokenCacheKey, out cached) && !string.IsNullOrEmpty(cached))
            {
                return cached!;
            }

            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
            {
                throw new PayPalApiException(
                    "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret.",
                    500, null, "MISSING_CREDENTIALS");
            }

            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw BuildApiException((int)response.StatusCode, content);
            }

            using var document = JsonDocument.Parse(content);
            var token = document.RootElement.GetProperty("access_token").GetString()!;
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s)
                ? s : 3000;

            // Refresh a minute early to avoid using a token that expires mid-request.
            _cache.Set(TokenCacheKey, token, TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60)));
            return token;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private PayPalApiException BuildApiException(int statusCode, string content)
    {
        string? issue = null, debugId = null, message = null;
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.TryGetProperty("name", out var name)) issue = name.GetString();
            if (root.TryGetProperty("debug_id", out var debug)) debugId = debug.GetString();
            if (root.TryGetProperty("message", out var msg)) message = msg.GetString();
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array &&
                details.GetArrayLength() > 0)
            {
                var first = details[0];
                // The specific issue (e.g. AUTHORIZATION_EXPIRED) is more actionable than the generic name.
                if (first.TryGetProperty("issue", out var iss) && iss.GetString() is { } issueValue) issue = issueValue;
                if (first.TryGetProperty("description", out var desc) && desc.GetString() is { } descValue) message = descValue;
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall back to raw content in the message.
        }

        var summary = message ?? (string.IsNullOrWhiteSpace(content) ? "(no response body)" : content);
        _logger.LogWarning($"PayPal API error {statusCode} ({issue ?? "unknown"}), debug_id {debugId ?? "n/a"}.");
        return new PayPalApiException($"PayPal returned {statusCode} {issue}: {summary}", statusCode, debugId, issue);
    }

    // --- request builders ---

    private static JsonObject BuildCardNode(PayPalCardDetails card, bool forVault)
    {
        var node = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode
        };
        if (!string.IsNullOrWhiteSpace(card.Name)) node["name"] = card.Name;
        if (card.BillingAddress is not null) node["billing_address"] = BuildAddressNode(card.BillingAddress);

        if (!forVault)
        {
            // SCA_WHEN_REQUIRED lets PayPal decide; a plain sandbox card is authorized without a challenge.
            node["attributes"] = new JsonObject
            {
                ["verification"] = new JsonObject { ["method"] = "SCA_WHEN_REQUIRED" }
            };
        }

        return node;
    }

    private static JsonObject BuildAddressNode(PayPalAddress address)
    {
        var node = new JsonObject { ["country_code"] = address.CountryCode };
        if (!string.IsNullOrWhiteSpace(address.AddressLine1)) node["address_line_1"] = address.AddressLine1;
        if (!string.IsNullOrWhiteSpace(address.AddressLine2)) node["address_line_2"] = address.AddressLine2;
        if (!string.IsNullOrWhiteSpace(address.AdminArea1)) node["admin_area_1"] = address.AdminArea1;
        if (!string.IsNullOrWhiteSpace(address.AdminArea2)) node["admin_area_2"] = address.AdminArea2;
        if (!string.IsNullOrWhiteSpace(address.PostalCode)) node["postal_code"] = address.PostalCode;
        return node;
    }

    // --- response parsers ---

    private static PayPalAuthorizationResult ParseAuthorization(JsonElement order, string payPalOrderId)
    {
        string? brand = null, last4 = null;
        if (order.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var card))
        {
            brand = GetString(card, "brand");
            last4 = GetString(card, "last_digits");
        }

        // A challenge means PayPal wants the shopper to approve in a browser — surface it, don't build a round-trip.
        if (RequiresBuyerApproval(order))
        {
            return new PayPalAuthorizationResult(payPalOrderId, string.Empty, "PAYER_ACTION_REQUIRED", null, brand, last4, true);
        }

        if (order.TryGetProperty("purchase_units", out var units) && units.ValueKind == JsonValueKind.Array &&
            units.GetArrayLength() > 0 &&
            units[0].TryGetProperty("payments", out var payments) &&
            payments.TryGetProperty("authorizations", out var auths) &&
            auths.ValueKind == JsonValueKind.Array && auths.GetArrayLength() > 0)
        {
            var auth = auths[0];
            var authId = auth.GetProperty("id").GetString()!;
            var (status, expiresAt) = ReadAuthorizationStatus(auth);
            return new PayPalAuthorizationResult(payPalOrderId, authId, status, expiresAt, brand, last4, false);
        }

        throw new PayPalApiException(
            "PayPal accepted the order but returned no authorization. The card may have been declined.",
            502, null, "NO_AUTHORIZATION_RETURNED");
    }

    private static (string status, DateTimeOffset? expiresAt) ReadAuthorizationStatus(JsonElement authorization)
    {
        var status = authorization.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        DateTimeOffset? expiresAt = null;
        if (authorization.TryGetProperty("expiration_time", out var exp) &&
            exp.ValueKind == JsonValueKind.String && exp.TryGetDateTimeOffset(out var parsed))
        {
            expiresAt = parsed;
        }
        return (status, expiresAt);
    }

    private static bool RequiresBuyerApproval(JsonElement order)
    {
        if (order.TryGetProperty("status", out var status) &&
            string.Equals(status.GetString(), "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (order.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                if (link.TryGetProperty("rel", out var rel) &&
                    string.Equals(rel.GetString(), "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static PayPalTransaction ParseTransaction(JsonElement detail)
    {
        var info = detail.TryGetProperty("transaction_info", out var ti) ? ti : detail;

        var transactionId = GetString(info, "transaction_id") ?? "";
        var eventCode = GetString(info, "transaction_event_code");
        var status = GetString(info, "transaction_status");
        var invoiceId = GetString(info, "invoice_id");
        var customField = GetString(info, "custom_field");

        decimal? amount = null;
        string? currency = null;
        if (info.TryGetProperty("transaction_amount", out var amt))
        {
            var (value, cur) = ReadMoney(amt);
            amount = value;
            currency = string.IsNullOrEmpty(cur) ? null : cur;
        }

        DateTimeOffset? initiated = null;
        if (info.TryGetProperty("transaction_initiation_date", out var d) &&
            d.ValueKind == JsonValueKind.String && d.TryGetDateTimeOffset(out var parsed))
        {
            initiated = parsed;
        }

        return new PayPalTransaction(transactionId, eventCode, status, amount, currency, invoiceId, customField, initiated);
    }

    // --- small helpers ---

    private static (decimal value, string currency) ReadMoney(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out var money) ? ReadMoney(money) : (0m, string.Empty);
    }

    private static (decimal value, string currency) ReadMoney(JsonElement money)
    {
        decimal value = 0m;
        if (money.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String &&
            decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
        }
        var currency = money.TryGetProperty("currency_code", out var c) ? c.GetString() ?? string.Empty : string.Empty;
        return (value, currency);
    }

    private static decimal? ReadOptionalMoney(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out _)) return null;
        var (value, _) = ReadMoney(parent, propertyName);
        return value;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value.Substring(0, maxLength);
}

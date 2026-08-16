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
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal REST client. Implements the full authorize → capture/void → refund lifecycle plus
/// card vaulting and transaction reporting, per the PayPal plugin's best-practices reference.
/// Card numbers are never logged.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly string _baseUrl;

    // The typed HttpClient registration makes this class transient, so the OAuth token is cached
    // process-wide (keyed by credentials + base URL) rather than per instance — a new token is not
    // fetched on every request. Access tokens live up to 8h; we refresh a minute early.
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? s_accessToken;
    private static string? s_tokenKey;
    private static DateTimeOffset s_tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalGateway(HttpClient http, IOptions<PayPalSettings> settings, ILogger<PayPalGateway> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
        _baseUrl = _settings.ResolveBaseUrl();
    }

    public string Currency => string.IsNullOrWhiteSpace(_settings.Currency) ? "USD" : _settings.Currency!;

    // ---------------------------------------------------------------- Authorize

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        JsonObject cardNode;
        if (!string.IsNullOrEmpty(request.VaultId))
        {
            cardNode = new JsonObject { ["vault_id"] = request.VaultId };
        }
        else if (request.Card is not null)
        {
            cardNode = BuildCardNode(request.Card);
        }
        else
        {
            throw new PaymentException("A card or a saved card is required to authorize a payment.", PaymentErrorReason.Validation);
        }

        var body = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["invoice_id"] = request.InvoiceId,
                    ["custom_id"] = request.CustomId,
                    ["amount"] = Money(request.Amount, request.Currency)
                }
            },
            ["payment_source"] = new JsonObject { ["card"] = cardNode }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, idempotencyKey, preferRepresentation: true, cancellationToken);
        var root = doc!.RootElement;

        var payPalOrderId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";

        (string? brand, string? last4) = ReadCardDescription(root);

        // A challenge that needs the shopper to approve in a browser.
        if (RequiresBuyerAction(root, status))
        {
            return new PayPalAuthorizationResult(payPalOrderId, string.Empty, status, null, RequiresBuyerAction: true, brand, last4);
        }

        var auth = FindFirstAuthorization(root);
        if (auth is null)
        {
            throw new PayPalApiException(
                $"PayPal did not return an authorization for order {payPalOrderId} (status {status}).",
                (int)HttpStatusCode.BadGateway, issue: null, debugId: null);
        }

        var authElement = auth.Value;
        var authId = authElement.GetProperty("id").GetString()!;
        var authStatus = authElement.TryGetProperty("status", out var asv) ? asv.GetString() ?? "" : "";
        DateTimeOffset? expires = ReadDate(authElement, "expiration_time");

        return new PayPalAuthorizationResult(payPalOrderId, authId, authStatus, expires, RequiresBuyerAction: false, brand, last4);
    }

    // ---------------------------------------------------------------- Get authorization

    public async Task<PayPalAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, preferRepresentation: false, cancellationToken);
        var root = doc!.RootElement;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
        var expires = ReadDate(root, "expiration_time");
        return new PayPalAuthorizationState(authorizationId, status, expires);
    }

    // ---------------------------------------------------------------- Capture

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = Money(amount, currency),
            ["final_capture"] = true
        };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, idempotencyKey, preferRepresentation: true, cancellationToken);
        var root = doc!.RootElement;

        var captureId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
        var gross = amount;
        var fee = 0m;
        var net = amount;

        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            gross = ReadMoney(breakdown, "gross_amount") ?? gross;
            fee = ReadMoney(breakdown, "paypal_fee") ?? 0m;
            net = ReadMoney(breakdown, "net_amount") ?? (gross - fee);
        }

        var capturedAt = ReadDate(root, "create_time") ?? DateTimeOffset.UtcNow;
        return new PayPalCaptureResult(captureId, status, gross, fee, net, capturedAt);
    }

    // ---------------------------------------------------------------- Reauthorize

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["amount"] = Money(amount, currency) };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, idempotencyKey, preferRepresentation: true, cancellationToken);
        var root = doc!.RootElement;

        var newAuthId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
        var expires = ReadDate(root, "expiration_time");
        return new PayPalAuthorizationResult(string.Empty, newAuthId, status, expires, RequiresBuyerAction: false, null, null);
    }

    // ---------------------------------------------------------------- Void

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, null, preferRepresentation: false, cancellationToken);
    }

    // ---------------------------------------------------------------- Refund

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject();
        if (amount is decimal a)
        {
            body["amount"] = Money(a, currency);
        }
        if (!string.IsNullOrWhiteSpace(noteToPayer))
        {
            body["note_to_payer"] = noteToPayer;
        }

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, idempotencyKey, preferRepresentation: true, cancellationToken);
        var root = doc!.RootElement;

        var refundId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
        var refundedAmount = ReadMoney(root, "amount") ?? amount ?? 0m;
        return new PayPalRefundResult(refundId, status, refundedAmount);
    }

    // ---------------------------------------------------------------- Vault

    public async Task<PayPalVaultResult> VaultCardAsync(PayPalCardDetails card, string? customerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["payment_source"] = new JsonObject { ["card"] = BuildCardNode(card) }
        };
        if (!string.IsNullOrEmpty(customerId))
        {
            body["customer"] = new JsonObject { ["id"] = customerId };
        }

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, idempotencyKey, preferRepresentation: false, cancellationToken);
        var root = doc!.RootElement;

        var vaultId = root.GetProperty("id").GetString()!;
        string? returnedCustomerId = null;
        if (root.TryGetProperty("customer", out var cust) && cust.TryGetProperty("id", out var cid))
        {
            returnedCustomerId = cid.GetString();
        }

        string brand = "UNKNOWN", last4 = "", expiry = card.Expiry, name = card.Name ?? "";
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var c))
        {
            brand = c.TryGetProperty("brand", out var b) ? b.GetString() ?? brand : brand;
            last4 = c.TryGetProperty("last_digits", out var l) ? l.GetString() ?? last4 : last4;
            expiry = c.TryGetProperty("expiry", out var e) ? e.GetString() ?? expiry : expiry;
            name = c.TryGetProperty("name", out var n) ? n.GetString() ?? name : name;
        }

        return new PayPalVaultResult(vaultId, returnedCustomerId, brand, last4, expiry, string.IsNullOrEmpty(name) ? null : name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, preferRepresentation: false, cancellationToken);
    }

    // ---------------------------------------------------------------- Reporting / reconciliation

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();

        // Reporting has no future data; clamp the end to now.
        var now = DateTimeOffset.UtcNow;
        var rangeEnd = to > now ? now : to;
        if (rangeEnd <= from)
        {
            return results;
        }

        // Chunk into <= 31-day windows (the API's hard limit).
        var windowStart = from;
        while (windowStart < rangeEnd)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > rangeEnd)
            {
                windowEnd = rangeEnd;
            }

            var page = 1;
            var totalPages = 1;
            do
            {
                var url = $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(FormatReportDate(windowStart))}" +
                          $"&end_date={Uri.EscapeDataString(FormatReportDate(windowEnd))}" +
                          $"&fields=transaction_info&page_size=500&page={page}";

                using var doc = await SendAsync(HttpMethod.Get, url, null, null, preferRepresentation: false, cancellationToken);
                var root = doc!.RootElement;

                if (root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var tpv))
                {
                    totalPages = tpv;
                }

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var td in details.EnumerateArray())
                    {
                        if (!td.TryGetProperty("transaction_info", out var info))
                        {
                            continue;
                        }

                        var txnId = info.TryGetProperty("transaction_id", out var ti) ? ti.GetString() ?? "" : "";
                        if (string.IsNullOrEmpty(txnId))
                        {
                            continue;
                        }

                        var invoiceId = info.TryGetProperty("invoice_id", out var inv) ? inv.GetString() : null;
                        var custom = info.TryGetProperty("custom_field", out var cf) ? cf.GetString() : null;
                        var status = info.TryGetProperty("transaction_status", out var ts) ? ts.GetString() ?? "" : "";
                        var eventCode = info.TryGetProperty("transaction_event_code", out var ec) ? ec.GetString() : null;
                        var date = ReadDate(info, "transaction_initiation_date") ?? ReadDate(info, "transaction_updated_date") ?? windowStart;

                        decimal amount = 0m;
                        string currency = Currency;
                        if (info.TryGetProperty("transaction_amount", out var ta))
                        {
                            amount = ReadMoneyValue(ta) ?? 0m;
                            currency = ta.TryGetProperty("currency_code", out var cc) ? cc.GetString() ?? currency : currency;
                        }

                        results.Add(new PayPalTransaction(txnId, invoiceId, custom, amount, currency, status, eventCode, date));
                    }
                }

                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd;
        }

        return results;
    }

    // ---------------------------------------------------------------- HTTP plumbing

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string path, JsonNode? body, string? idempotencyKey, bool preferRepresentation, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var req = new HttpRequestMessage(method, AbsoluteUrl(path));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            req.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (preferRepresentation)
        {
            req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (body is not null)
        {
            req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var resp = await _http.SendAsync(req, cancellationToken);
        var payload = await resp.Content.ReadAsStringAsync(cancellationToken);

        _logger.LogInformation("PayPal {Method} {Path} -> {Status}", method, path, (int)resp.StatusCode);

        if (!resp.IsSuccessStatusCode)
        {
            throw BuildApiException(resp, payload);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }
        return JsonDocument.Parse(payload);
    }

    private PayPalApiException BuildApiException(HttpResponseMessage resp, string payload)
    {
        string? issue = null;
        string? debugId = resp.Headers.TryGetValues("Paypal-Debug-Id", out var vals) ? System.Linq.Enumerable.FirstOrDefault(vals) : null;
        string message = $"PayPal call failed with {(int)resp.StatusCode}.";

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("debug_id", out var d))
            {
                debugId ??= d.GetString();
            }
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0)
            {
                var first = details[0];
                issue = first.TryGetProperty("issue", out var iss) ? iss.GetString() : null;
                var desc = first.TryGetProperty("description", out var ds) ? ds.GetString() : null;
                message = $"PayPal error {(int)resp.StatusCode} {issue}: {desc}";
            }
            else if (root.TryGetProperty("message", out var m))
            {
                message = $"PayPal error {(int)resp.StatusCode}: {m.GetString()}";
            }
            else if (root.TryGetProperty("error_description", out var ed))
            {
                message = $"PayPal error {(int)resp.StatusCode}: {ed.GetString()}";
            }
        }
        catch (JsonException)
        {
            // non-JSON error body; keep the generic message
        }

        _logger.LogWarning("PayPal error {Status} issue={Issue} debug_id={DebugId}", (int)resp.StatusCode, issue, debugId);
        return new PayPalApiException(message, (int)resp.StatusCode, issue, debugId);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var tokenKey = $"{_settings.ClientId}@{_baseUrl}";

        if (s_accessToken is not null && s_tokenKey == tokenKey && DateTimeOffset.UtcNow < s_tokenExpiresAt)
        {
            return s_accessToken;
        }

        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (s_accessToken is not null && s_tokenKey == tokenKey && DateTimeOffset.UtcNow < s_tokenExpiresAt)
            {
                return s_accessToken;
            }

            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
            {
                throw new PaymentException(
                    "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret (via user-secrets or environment).",
                    PaymentErrorReason.ProviderError);
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, AbsoluteUrl("/v1/oauth2/token"));
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            req.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });

            using var resp = await _http.SendAsync(req, cancellationToken);
            var payload = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                throw BuildApiException(resp, payload);
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            s_accessToken = root.GetProperty("access_token").GetString();
            s_tokenKey = tokenKey;
            var expiresIn = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var secs) ? secs : 3000;
            // refresh a minute early
            s_tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            return s_accessToken!;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    // ---------------------------------------------------------------- JSON helpers

    private static JsonObject BuildCardNode(PayPalCardDetails card)
    {
        var node = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry
        };
        if (!string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            node["security_code"] = card.SecurityCode;
        }
        if (!string.IsNullOrWhiteSpace(card.Name))
        {
            node["name"] = card.Name;
        }
        if (card.BillingAddress is { } addr)
        {
            var a = new JsonObject();
            if (!string.IsNullOrWhiteSpace(addr.AddressLine1)) a["address_line_1"] = addr.AddressLine1;
            if (!string.IsNullOrWhiteSpace(addr.AddressLine2)) a["address_line_2"] = addr.AddressLine2;
            if (!string.IsNullOrWhiteSpace(addr.AdminArea2)) a["admin_area_2"] = addr.AdminArea2;
            if (!string.IsNullOrWhiteSpace(addr.AdminArea1)) a["admin_area_1"] = addr.AdminArea1;
            if (!string.IsNullOrWhiteSpace(addr.PostalCode)) a["postal_code"] = addr.PostalCode;
            if (!string.IsNullOrWhiteSpace(addr.CountryCode)) a["country_code"] = addr.CountryCode;
            if (a.Count > 0) node["billing_address"] = a;
        }
        return node;
    }

    private static JsonObject Money(decimal value, string currency) => new()
    {
        ["currency_code"] = currency,
        ["value"] = value.ToString("F2", CultureInfo.InvariantCulture)
    };

    private static (string? brand, string? last4) ReadCardDescription(JsonElement root)
    {
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var c))
        {
            var brand = c.TryGetProperty("brand", out var b) ? b.GetString() : null;
            var last4 = c.TryGetProperty("last_digits", out var l) ? l.GetString() : null;
            return (brand, last4);
        }
        return (null, null);
    }

    private static JsonElement? FindFirstAuthorization(JsonElement orderRoot)
    {
        if (!orderRoot.TryGetProperty("purchase_units", out var pus) || pus.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (var pu in pus.EnumerateArray())
        {
            if (pu.TryGetProperty("payments", out var payments) &&
                payments.TryGetProperty("authorizations", out var auths) &&
                auths.ValueKind == JsonValueKind.Array &&
                auths.GetArrayLength() > 0)
            {
                return auths[0];
            }
        }
        return null;
    }

    private static bool RequiresBuyerAction(JsonElement orderRoot, string status)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (orderRoot.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
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

    private static decimal? ReadMoney(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var el) ? ReadMoneyValue(el) : null;

    private static decimal? ReadMoneyValue(JsonElement moneyElement)
    {
        if (moneyElement.TryGetProperty("value", out var v))
        {
            var s = v.GetString();
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
            {
                return d;
            }
        }
        return null;
    }

    private static DateTimeOffset? ReadDate(JsonElement parent, string propertyName)
    {
        if (parent.TryGetProperty(propertyName, out var el) && el.ValueKind == JsonValueKind.String)
        {
            if (DateTimeOffset.TryParse(el.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            {
                return dt;
            }
        }
        return null;
    }

    private static string FormatReportDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private string AbsoluteUrl(string path) => path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : _baseUrl + path;
}

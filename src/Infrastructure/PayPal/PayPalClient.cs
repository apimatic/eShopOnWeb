using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Talks to PayPal's REST API directly (Orders v2, Payments v2, Vault v3, Reporting v1) — the
/// approach the PayPal plugin sanctions for full control over request structure. Manages the
/// OAuth token (cached and refreshed proactively) and never logs card data.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalClient> _logger;
    private readonly string _baseUrl;

    // Access token is shared across requests for the (single) merchant credentials.
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);
    private static string? _cachedToken;
    private static DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public PayPalClient(HttpClient http, PayPalSettings settings, ILogger<PayPalClient> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
        _baseUrl = settings.ResolveBaseUrl();
    }

    // ------------------------------------------------------------------ Authorization (hold)

    public Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(
        decimal amount, string currency, CardDetails card, string reference, string idempotencyKey, CancellationToken ct = default)
    {
        var cardNode = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode
        };
        if (!string.IsNullOrWhiteSpace(card.Name)) cardNode["name"] = card.Name;
        var billing = BuildBillingAddress(card.BillingAddress);
        if (billing is not null) cardNode["billing_address"] = billing;

        var paymentSource = new JsonObject { ["card"] = cardNode };
        return CreateAuthorizedOrderAsync(amount, currency, paymentSource, reference, idempotencyKey, ct);
    }

    public Task<PayPalAuthorizationResult> AuthorizeWithVaultTokenAsync(
        decimal amount, string currency, string vaultTokenId, string reference, string idempotencyKey, CancellationToken ct = default)
    {
        var paymentSource = new JsonObject
        {
            ["token"] = new JsonObject
            {
                ["id"] = vaultTokenId,
                ["type"] = "PAYMENT_METHOD_TOKEN"
            }
        };
        return CreateAuthorizedOrderAsync(amount, currency, paymentSource, reference, idempotencyKey, ct);
    }

    private async Task<PayPalAuthorizationResult> CreateAuthorizedOrderAsync(
        decimal amount, string currency, JsonObject paymentSource, string reference, string idempotencyKey, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["reference_id"] = "default",
                    ["invoice_id"] = reference,
                    ["custom_id"] = reference,
                    ["amount"] = new JsonObject
                    {
                        ["currency_code"] = currency,
                        ["value"] = Money(amount)
                    }
                }
            },
            ["payment_source"] = paymentSource
        };

        using var response = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, idempotencyKey, ct);
        var doc = await ReadJsonAsync(response, ct);
        EnsureSuccess(response, doc, "create authorized order");

        var root = doc!.RootElement;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() : null;

        // A challenge that needs browser approval — stop rather than build an approval round-trip.
        if (RequiresPayerAction(root, status))
            throw new PayPalChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (3-D Secure / payer action). Stopping as instructed.");

        var orderId = root.GetProperty("id").GetString()!;
        var authorization = FindAuthorization(root);
        if (authorization is null)
            throw new PayPalApiException(
                $"PayPal did not return an authorization for order {orderId} (status {status}).",
                (int)response.StatusCode, TryGetDebugId(root));

        var authId = authorization.Value.GetProperty("id").GetString()!;
        var authStatus = authorization.Value.TryGetProperty("status", out var asv) ? asv.GetString()! : "CREATED";

        _logger.LogInformation("PayPal authorized order {OrderId} (authorization {AuthId}, status {Status}).", orderId, authId, authStatus);
        return new PayPalAuthorizationResult(orderId, authId, authStatus);
    }

    // ------------------------------------------------------------------ Capture (at fulfilment)

    public async Task<string> GetAuthorizationStatusAsync(string authorizationId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, ct);
        var doc = await ReadJsonAsync(response, ct);
        EnsureSuccess(response, doc, "get authorization");
        return doc!.RootElement.TryGetProperty("status", out var st) ? st.GetString() ?? "UNKNOWN" : "UNKNOWN";
    }

    public async Task<PayPalCaptureOutcome> CaptureAuthorizationAsync(
        string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        var status = await GetAuthorizationStatusAsync(authorizationId, ct);

        var renewed = false;
        string? renewedAuthId = null;
        string? renewedStatus = null;
        var activeAuthId = authorizationId;

        // Renew a hold that has gone stale before fulfilment rather than failing outright.
        if (IsRenewableStale(status))
        {
            (renewedAuthId, renewedStatus) = await ReauthorizeAsync(authorizationId, amount, currency, idempotencyKey, ct);
            renewed = true;
            activeAuthId = renewedAuthId;
        }
        else if (!CanCapture(status))
        {
            throw new AuthorizationCannotBeRenewedException(
                $"The authorization for this order is in state '{status}' and can no longer be captured or renewed. Ask the shopper to place and pay for the order again.");
        }

        var capture = await CaptureInternalAsync(activeAuthId, amount, currency, idempotencyKey, ct);
        return new PayPalCaptureOutcome(capture, renewed, renewedAuthId, renewedStatus);
    }

    private async Task<(string authId, string status)> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["amount"] = new JsonObject { ["currency_code"] = currency, ["value"] = Money(amount) }
        };

        HttpResponseMessage response;
        try
        {
            response = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize",
                body, $"reauth-{idempotencyKey}", ct, preferRepresentation: true);
        }
        catch (PayPalApiException ex)
        {
            throw new AuthorizationCannotBeRenewedException(
                $"The authorization for this order has expired and could not be renewed ({ex.Message}). Ask the shopper to place and pay for the order again.");
        }

        using (response)
        {
            var doc = await ReadJsonAsync(response, ct);
            if (!response.IsSuccessStatusCode)
            {
                var issue = TryGetIssue(doc?.RootElement);
                throw new AuthorizationCannotBeRenewedException(
                    $"The authorization for this order has expired and could not be renewed ({issue ?? response.StatusCode.ToString()}). Ask the shopper to place and pay for the order again.");
            }

            var root = doc!.RootElement;
            var newAuthId = root.TryGetProperty("id", out var id) ? id.GetString()! : authorizationId;
            var newStatus = root.TryGetProperty("status", out var st) ? st.GetString()! : "CREATED";
            _logger.LogInformation("PayPal reauthorized authorization {OldAuth} -> {NewAuth} ({Status}).", authorizationId, newAuthId, newStatus);
            return (newAuthId, newStatus);
        }
    }

    private async Task<PayPalCaptureResult> CaptureInternalAsync(
        string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["amount"] = new JsonObject { ["currency_code"] = currency, ["value"] = Money(amount) },
            ["final_capture"] = true
        };

        using var response = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture",
            body, $"capture-{idempotencyKey}", ct, preferRepresentation: true);
        var doc = await ReadJsonAsync(response, ct);
        EnsureSuccess(response, doc, "capture authorization");

        var root = doc!.RootElement;
        var captureId = root.GetProperty("id").GetString()!;
        var captureStatus = root.TryGetProperty("status", out var st) ? st.GetString()! : "COMPLETED";

        // The fee/net breakdown may not be in the capture response — read it back to be sure.
        var (gross, fee, net, cur) = ExtractBreakdown(root);
        if (fee is null || net is null)
        {
            var detail = await GetCaptureAsync(captureId, ct);
            gross = detail.gross ?? gross;
            fee = detail.fee ?? fee;
            net = detail.net ?? net;
            cur = detail.currency ?? cur;
        }

        _logger.LogInformation("PayPal captured {CaptureId} (status {Status}, gross {Gross}, fee {Fee}, net {Net}).",
            captureId, captureStatus, gross, fee, net);

        return new PayPalCaptureResult(
            captureId, captureStatus,
            gross ?? amount,
            fee ?? 0m,
            net ?? (gross ?? amount),
            cur ?? currency);
    }

    private async Task<(decimal? gross, decimal? fee, decimal? net, string? currency)> GetCaptureAsync(string captureId, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/v2/payments/captures/{captureId}", null, null, ct);
        var doc = await ReadJsonAsync(response, ct);
        EnsureSuccess(response, doc, "get capture");
        return ExtractBreakdown(doc!.RootElement);
    }

    // ------------------------------------------------------------------ Cancel (void the hold)

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void",
            null, $"void-{idempotencyKey}", ct);
        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("PayPal voided authorization {AuthId}.", authorizationId);
            return;
        }
        var doc = await ReadJsonAsync(response, ct);
        EnsureSuccess(response, doc, "void authorization");
    }

    // ------------------------------------------------------------------ Refund

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        var body = new JsonObject();
        if (amount is not null)
        {
            body["amount"] = new JsonObject { ["currency_code"] = currency, ["value"] = Money(amount.Value) };
        }

        using var response = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund",
            body, idempotencyKey, ct, preferRepresentation: true);
        var doc = await ReadJsonAsync(response, ct);
        EnsureSuccess(response, doc, "refund capture");

        var root = doc!.RootElement;
        var refundId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString()! : "COMPLETED";

        decimal refundedAmount;
        string refundedCurrency = currency;
        if (TryReadAmount(root, "amount", out var amt, out var cur))
        {
            refundedAmount = amt;
            refundedCurrency = cur ?? currency;
        }
        else if (amount is not null)
        {
            refundedAmount = amount.Value;
        }
        else
        {
            // Full refund without a returned amount — read it back.
            var detail = await GetRefundAmountAsync(refundId, ct);
            refundedAmount = detail.amount ?? 0m;
            refundedCurrency = detail.currency ?? currency;
        }

        _logger.LogInformation("PayPal refund {RefundId} for capture {CaptureId} ({Status}, {Amount} {Currency}).",
            refundId, captureId, status, refundedAmount, refundedCurrency);
        return new PayPalRefundResult(refundId, status, refundedAmount, refundedCurrency);
    }

    private async Task<(decimal? amount, string? currency)> GetRefundAmountAsync(string refundId, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/v2/payments/refunds/{refundId}", null, null, ct);
        var doc = await ReadJsonAsync(response, ct);
        EnsureSuccess(response, doc, "get refund");
        return TryReadAmount(doc!.RootElement, "amount", out var amt, out var cur) ? (amt, cur) : (null, null);
    }

    // ------------------------------------------------------------------ Vault (saved cards)

    public async Task<VaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken ct = default)
    {
        var cardNode = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode
        };
        if (!string.IsNullOrWhiteSpace(card.Name)) cardNode["name"] = card.Name;
        var billing = BuildBillingAddress(card.BillingAddress);
        if (billing is not null) cardNode["billing_address"] = billing;

        var body = new JsonObject { ["payment_source"] = new JsonObject { ["card"] = cardNode } };

        using var response = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, idempotencyKey, ct);
        var doc = await ReadJsonAsync(response, ct);
        EnsureSuccess(response, doc, "vault card");

        var root = doc!.RootElement;
        var vaultId = root.GetProperty("id").GetString()!;
        string? customerId = root.TryGetProperty("customer", out var cust) && cust.TryGetProperty("id", out var cid) ? cid.GetString() : null;

        string brand = "CARD", last4 = "----", expiry = "", name = card.Name ?? "";
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var c))
        {
            if (c.TryGetProperty("brand", out var b)) brand = b.GetString() ?? brand;
            if (c.TryGetProperty("last_digits", out var l)) last4 = l.GetString() ?? last4;
            if (c.TryGetProperty("expiry", out var e)) expiry = e.GetString() ?? expiry;
            if (c.TryGetProperty("name", out var n)) name = n.GetString() ?? name;
        }

        _logger.LogInformation("PayPal vaulted a {Brand} card ending {Last4} (token {VaultId}).", brand, last4, vaultId);
        return new VaultedCard(vaultId, customerId, brand, last4, expiry, string.IsNullOrWhiteSpace(name) ? null : name);
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultTokenId}", null, null, ct);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("PayPal deleted vault token {VaultId} (status {Status}).", vaultTokenId, (int)response.StatusCode);
            return;
        }
        var doc = await ReadJsonAsync(response, ct);
        EnsureSuccess(response, doc, "delete vault token");
    }

    // ------------------------------------------------------------------ Reporting (reconciliation)

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<PayPalTransaction>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Reporting can't look into the future; cap the end at "now".
        var now = DateTimeOffset.UtcNow;
        var rangeEnd = to > now ? now : to;
        if (rangeEnd <= from)
            return results;

        // PayPal's reporting range is capped at 31 days — walk the whole range in windows.
        var windowStart = from;
        while (windowStart < rangeEnd)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > rangeEnd) windowEnd = rangeEnd;

            var page = 1;
            int totalPages;
            do
            {
                var query =
                    $"?start_date={Iso(windowStart)}&end_date={Iso(windowEnd)}&fields=all&page_size=500&page={page}";
                using var response = await SendAsync(HttpMethod.Get, "/v1/reporting/transactions" + query, null, null, ct);
                var doc = await ReadJsonAsync(response, ct);
                EnsureSuccess(response, doc, "list transactions");

                var root = doc!.RootElement;
                totalPages = root.TryGetProperty("total_pages", out var tp) ? tp.GetInt32() : 1;

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in details.EnumerateArray())
                    {
                        var mapped = MapTransaction(t);
                        if (mapped is not null && seen.Add(mapped.TransactionId + "|" + mapped.EventCode))
                            results.Add(mapped);
                    }
                }
                page++;
            }
            while (page <= totalPages);

            // Nudge the next window forward so consecutive windows don't overlap on the boundary second.
            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    private static PayPalTransaction? MapTransaction(JsonElement detail)
    {
        if (!detail.TryGetProperty("transaction_info", out var info)) return null;

        string GetStr(string name) => info.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
        var txnId = GetStr("transaction_id");
        if (string.IsNullOrEmpty(txnId)) return null;

        decimal amount = TryReadAmount(info, "transaction_amount", out var a, out var cur) ? a : 0m;
        decimal fee = TryReadAmount(info, "fee_amount", out var f, out _) ? f : 0m;

        DateTimeOffset? initiated = null;
        if (info.TryGetProperty("transaction_initiation_date", out var d) &&
            DateTimeOffset.TryParse(d.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            initiated = parsed;

        var invoiceId = info.TryGetProperty("invoice_id", out var inv) ? inv.GetString() : null;
        var custom = info.TryGetProperty("custom_field", out var cf) ? cf.GetString() : null;

        return new PayPalTransaction(
            txnId,
            info.TryGetProperty("paypal_reference_id", out var rf) ? rf.GetString() : null,
            !string.IsNullOrWhiteSpace(invoiceId) ? invoiceId : custom,
            info.TryGetProperty("transaction_status", out var s) ? s.GetString() : null,
            info.TryGetProperty("transaction_event_code", out var ec) ? ec.GetString() : null,
            amount,
            fee,
            cur,
            initiated);
    }

    // ------------------------------------------------------------------ HTTP + token plumbing

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, JsonNode? body, string? idempotencyKey, CancellationToken ct, bool preferRepresentation = false)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            var token = await GetAccessTokenAsync(ct);
            using var request = new HttpRequestMessage(method, _baseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (!string.IsNullOrEmpty(idempotencyKey))
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
            if (preferRepresentation)
                request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (body is not null)
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request, ct);

            // Back off and retry on rate limiting / transient server errors.
            if ((response.StatusCode == (HttpStatusCode)429 || (int)response.StatusCode >= 500) && attempt < maxAttempts)
            {
                var delay = RetryDelay(response, attempt);
                response.Dispose();
                _logger.LogWarning("PayPal {Method} {Path} returned {Status}; retrying in {Delay}ms (attempt {Attempt}).",
                    method, path, "transient", delay.TotalMilliseconds, attempt);
                await Task.Delay(delay, ct);
                continue;
            }

            return response;
        }
    }

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values) &&
            int.TryParse(values.FirstOrDefault(), out var seconds))
            return TimeSpan.FromSeconds(Math.Min(seconds, 10));
        return TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            return _cachedToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
                return _cachedToken;

            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _http.SendAsync(request, ct);
            var doc = await ReadJsonAsync(response, ct);
            if (!response.IsSuccessStatusCode)
                throw new PayPalApiException(
                    $"PayPal token request failed ({(int)response.StatusCode}).",
                    (int)response.StatusCode, TryGetDebugId(doc?.RootElement));

            var root = doc!.RootElement;
            var token = root.GetProperty("access_token").GetString()!;
            var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;

            _cachedToken = token;
            // Refresh a minute early rather than on the exact boundary.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn - 60));
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    // ------------------------------------------------------------------ helpers

    private static string Money(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static string Iso(DateTimeOffset value) =>
        Uri.EscapeDataString(value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

    private static JsonObject? BuildBillingAddress(CardBillingAddress? a)
    {
        if (a is null) return null;
        var node = new JsonObject();
        if (!string.IsNullOrWhiteSpace(a.AddressLine1)) node["address_line_1"] = a.AddressLine1;
        if (!string.IsNullOrWhiteSpace(a.AddressLine2)) node["address_line_2"] = a.AddressLine2;
        if (!string.IsNullOrWhiteSpace(a.AdminArea2)) node["admin_area_2"] = a.AdminArea2;
        if (!string.IsNullOrWhiteSpace(a.AdminArea1)) node["admin_area_1"] = a.AdminArea1;
        if (!string.IsNullOrWhiteSpace(a.PostalCode)) node["postal_code"] = a.PostalCode;
        if (!string.IsNullOrWhiteSpace(a.CountryCode)) node["country_code"] = a.CountryCode;
        return node.Count > 0 ? node : null;
    }

    private static JsonElement? FindAuthorization(JsonElement orderRoot)
    {
        if (!orderRoot.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments) &&
                payments.TryGetProperty("authorizations", out var auths) &&
                auths.ValueKind == JsonValueKind.Array)
            {
                foreach (var auth in auths.EnumerateArray())
                    return auth;
            }
        }
        return null;
    }

    private static bool RequiresPayerAction(JsonElement root, string? status)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            return true;
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                if (link.TryGetProperty("rel", out var rel) &&
                    string.Equals(rel.GetString(), "payer-action", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private static (decimal? gross, decimal? fee, decimal? net, string? currency) ExtractBreakdown(JsonElement captureRoot)
    {
        decimal? gross = null, fee = null, net = null;
        string? currency = null;
        if (TryReadAmount(captureRoot, "amount", out var amt, out var cur)) { gross = amt; currency = cur; }

        if (captureRoot.TryGetProperty("seller_receivable_breakdown", out var b))
        {
            if (TryReadAmount(b, "gross_amount", out var g, out var gc)) { gross = g; currency ??= gc; }
            if (TryReadAmount(b, "paypal_fee", out var f, out _)) fee = f;
            if (TryReadAmount(b, "net_amount", out var n, out _)) net = n;
        }
        return (gross, fee, net, currency);
    }

    private static bool TryReadAmount(JsonElement parent, string property, out decimal value, out string? currency)
    {
        value = 0m; currency = null;
        if (!parent.TryGetProperty(property, out var amt) || amt.ValueKind != JsonValueKind.Object)
            return false;
        if (amt.TryGetProperty("currency_code", out var c)) currency = c.GetString();
        if (amt.TryGetProperty("value", out var v) &&
            decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }
        return false;
    }

    private static bool IsRenewableStale(string status) =>
        string.Equals(status, "EXPIRED", StringComparison.OrdinalIgnoreCase);

    private static bool CanCapture(string status) =>
        string.Equals(status, "CREATED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "PENDING", StringComparison.OrdinalIgnoreCase);

    private static async Task<JsonDocument?> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(content)) return null;
        try { return JsonDocument.Parse(content); }
        catch (JsonException) { return null; }
    }

    private void EnsureSuccess(HttpResponseMessage response, JsonDocument? doc, string operation)
    {
        if (response.IsSuccessStatusCode) return;

        var root = doc?.RootElement;
        var debugId = TryGetDebugId(root);
        var issue = TryGetIssue(root);
        var message = root is not null && root.Value.TryGetProperty("message", out var m) ? m.GetString() : null;

        // Log the debug id (never the request body/card) — it is required for PayPal support.
        _logger.LogWarning("PayPal {Operation} failed: {Status} {Issue} (debug_id {DebugId}).",
            operation, (int)response.StatusCode, issue ?? "-", debugId ?? "-");

        if (string.Equals(issue, "INSTRUMENT_DECLINED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(issue, "CARD_EXPIRED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(issue, "PAYMENT_DENIED", StringComparison.OrdinalIgnoreCase))
            throw new PayPalInstrumentDeclinedException(
                $"The card was declined by PayPal ({issue}). Please use a different card.", debugId);

        throw new PayPalApiException(
            $"PayPal {operation} failed ({(int)response.StatusCode}{(issue is null ? "" : $"/{issue}")}{(message is null ? "" : $": {message}")}).",
            (int)response.StatusCode, debugId, issue);
    }

    private static string? TryGetDebugId(JsonElement? root) =>
        root is not null && root.Value.TryGetProperty("debug_id", out var d) ? d.GetString() : null;

    private static string? TryGetIssue(JsonElement? root)
    {
        if (root is null) return null;
        if (root.Value.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
        {
            foreach (var detail in details.EnumerateArray())
            {
                if (detail.TryGetProperty("issue", out var issue))
                    return issue.GetString();
            }
        }
        if (root.Value.TryGetProperty("name", out var name))
            return name.GetString();
        return null;
    }
}

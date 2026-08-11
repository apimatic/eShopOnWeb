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
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal REST API client (Orders v2, Payments v2, Payment Method Tokens v3, Transaction Search v1).
/// Server-to-server; card details are used only to build the outgoing request and are never persisted or logged.
/// Endpoints and shapes were confirmed against the live PayPal sandbox before implementation.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // PayPal transaction-search limits: 31-day window per query, 500 records per page.
    private const int MaxWindowDays = 31;
    private const int ReportingPageSize = 500;

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalClient> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(HttpClient http, PayPalSettings settings, IAppLogger<PayPalClient> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
    }

    public string Currency => _settings.ResolvedCurrency;

    // ---------------- Orders: authorize ----------------

    public async Task<AuthorizationOutcome> CreateAuthorizedOrderWithCardAsync(
        Money amount, CardDetails card, string referenceId, string idempotencyKey, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[] { PurchaseUnit(amount, referenceId) },
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = CardBody(card) }
        };
        using var doc = await SendJsonAsync(HttpMethod.Post, "v2/checkout/orders", body, idempotencyKey, representation: true, ct);
        return ReadAuthorization(doc.RootElement);
    }

    public async Task<AuthorizationOutcome> CreateAuthorizedOrderWithVaultAsync(
        Money amount, string vaultId, string referenceId, string idempotencyKey, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[] { PurchaseUnit(amount, referenceId) },
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = new Dictionary<string, object?> { ["vault_id"] = vaultId }
            }
        };
        using var doc = await SendJsonAsync(HttpMethod.Post, "v2/checkout/orders", body, idempotencyKey, representation: true, ct);
        return ReadAuthorization(doc.RootElement);
    }

    public async Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, $"v2/payments/authorizations/{authorizationId}", null, null, representation: false, ct);
        var root = doc.RootElement;
        return new AuthorizationSnapshot(
            root.GetProperty("id").GetString()!,
            root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "",
            ReadDate(root, "expiration_time"));
    }

    // ---------------- Payments: capture / reauthorize / void ----------------

    public async Task<CaptureOutcome> CaptureAuthorizationAsync(
        string authorizationId, Money amount, string idempotencyKey, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = AmountBody(amount),
            ["final_capture"] = true
        };
        using var doc = await SendJsonAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/capture",
            body, idempotencyKey, representation: true, ct);

        var root = doc.RootElement;
        var captureId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";

        if (TryReadBreakdown(root, out var gross, out var fee, out var net, out var currency))
            return new CaptureOutcome(captureId, status, gross, fee, net, currency);

        // Minimal response — fetch the full capture to obtain the fee breakdown.
        using var full = await SendJsonAsync(HttpMethod.Get, $"v2/payments/captures/{captureId}", null, null, representation: false, ct);
        TryReadBreakdown(full.RootElement, out gross, out fee, out net, out currency);
        var fullStatus = full.RootElement.TryGetProperty("status", out var fs) ? fs.GetString() ?? status : status;
        return new CaptureOutcome(captureId, fullStatus, gross, fee, net, string.IsNullOrEmpty(currency) ? amount.Currency : currency);
    }

    public async Task<AuthorizationOutcome> ReauthorizeAsync(
        string authorizationId, Money amount, string idempotencyKey, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["amount"] = AmountBody(amount) };
        using var doc = await SendJsonAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/reauthorize",
            body, idempotencyKey, representation: true, ct);
        var root = doc.RootElement;
        return new AuthorizationOutcome(
            PayPalOrderId: "",
            AuthorizationId: root.GetProperty("id").GetString()!,
            Status: root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "",
            ExpiresAt: ReadDate(root, "expiration_time"),
            Card: null);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void",
            null, null, representation: false, ct);
    }

    // ---------------- Payments: refund ----------------

    public async Task<RefundOutcome> RefundCaptureAsync(
        string captureId, Money? amount, string idempotencyKey, CancellationToken ct = default)
    {
        // Empty body = full refund; amount object = partial refund.
        object? body = amount is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?> { ["amount"] = AmountBody(amount) };

        using var doc = await SendJsonAsync(HttpMethod.Post, $"v2/payments/captures/{captureId}/refund",
            body, idempotencyKey, representation: true, ct);
        var root = doc.RootElement;
        var refundId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        decimal value = amount?.Amount ?? 0m;
        string currency = amount?.Currency ?? Currency;
        if (root.TryGetProperty("amount", out var amt))
        {
            value = ParseDecimal(amt, "value");
            if (amt.TryGetProperty("currency_code", out var cc)) currency = cc.GetString() ?? currency;
        }
        return new RefundOutcome(refundId, status, value, currency);
    }

    // ---------------- Vault: save / delete card ----------------

    public async Task<VaultedCardResult> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = CardBody(card) }
        };
        using var doc = await SendJsonAsync(HttpMethod.Post, "v3/vault/payment-tokens", body, idempotencyKey, representation: true, ct);
        var root = doc.RootElement;

        var vaultId = root.GetProperty("id").GetString()!;
        string? customerId = root.TryGetProperty("customer", out var cust) && cust.TryGetProperty("id", out var cid)
            ? cid.GetString() : null;

        string brand = "CARD", last = "", expiry = card.Expiry;
        string? name = card.CardholderName;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            if (cardEl.TryGetProperty("brand", out var b) && b.ValueKind == JsonValueKind.String) brand = b.GetString()!;
            if (cardEl.TryGetProperty("last_digits", out var ld) && ld.ValueKind == JsonValueKind.String) last = ld.GetString()!;
            if (cardEl.TryGetProperty("expiry", out var ex) && ex.ValueKind == JsonValueKind.String) expiry = ex.GetString()!;
            if (cardEl.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String) name = nm.GetString();
        }
        if (string.IsNullOrEmpty(last)) last = LastFour(card.Number);
        return new VaultedCardResult(vaultId, customerId, brand, last, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"v3/vault/payment-tokens/{vaultId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, ct);
        // A token that is already gone is fine — deletion is idempotent.
        if (response.StatusCode == HttpStatusCode.NotFound) return;
        if (!response.IsSuccessStatusCode)
            await ThrowFromResponseAsync(response, ct);
    }

    // ---------------- Reporting: transaction search ----------------

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<PayPalTransactionRecord>();

        // Cover the whole range by chunking into <=31-day windows and paging each to the end.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(MaxWindowDays);
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            int totalPages;
            do
            {
                var url = "v1/reporting/transactions"
                    + $"?start_date={Uri.EscapeDataString(FormatReportingDate(windowStart))}"
                    + $"&end_date={Uri.EscapeDataString(FormatReportingDate(windowEnd))}"
                    + $"&fields=all&page_size={ReportingPageSize}&page={page}";

                using var doc = await SendReportingAsync(url, ct);
                var root = doc.RootElement;
                totalPages = root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var tpv) ? tpv : 1;

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d in details.EnumerateArray())
                    {
                        if (d.TryGetProperty("transaction_info", out var info))
                            results.Add(ReadTransaction(info));
                    }
                }
                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd;
        }

        return results;
    }

    // ---------------- request/body helpers ----------------

    private static Dictionary<string, object?> PurchaseUnit(Money amount, string referenceId) => new()
    {
        ["custom_id"] = referenceId,
        ["amount"] = AmountBody(amount)
    };

    private static Dictionary<string, object?> AmountBody(Money amount) => new()
    {
        ["currency_code"] = amount.Currency,
        ["value"] = Fmt(amount.Amount)
    };

    private static Dictionary<string, object?> CardBody(CardDetails card)
    {
        // Note: System.Text.Json's WhenWritingNull does not drop null Dictionary VALUES, so we must omit
        // absent fields here — PayPal's card processor refuses a billing address carrying explicit nulls.
        var body = new Dictionary<string, object?>
        {
            ["number"] = new string((card.Number ?? "").Where(char.IsDigit).ToArray()),
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["name"] = card.CardholderName
        };
        var b = card.BillingAddress;
        if (b is not null)
        {
            var addr = new Dictionary<string, object?>();
            AddIfPresent(addr, "address_line_1", b.AddressLine1);
            AddIfPresent(addr, "address_line_2", b.AddressLine2);
            AddIfPresent(addr, "admin_area_2", b.City);
            AddIfPresent(addr, "admin_area_1", b.State);
            AddIfPresent(addr, "postal_code", b.PostalCode);
            AddIfPresent(addr, "country_code", b.CountryCode);
            if (addr.Count > 0)
                body["billing_address"] = addr;
        }
        return body;
    }

    private static void AddIfPresent(Dictionary<string, object?> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            target[key] = value;
    }

    // ---------------- response parsing ----------------

    private static AuthorizationOutcome ReadAuthorization(JsonElement root)
    {
        var orderId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";

        // A card that needs 3-D Secure / shopper approval comes back as PAYER_ACTION_REQUIRED with a payer-action link.
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) || HasPayerActionLink(root))
            throw new PayPalChallengeRequiredException();

        if (!TryReadFirstAuthorization(root, out var authId, out var authStatus, out var expiresAt))
            throw new PayPalApiException(502, "NO_AUTHORIZATION",
                $"PayPal did not return an authorization for the order (status {status}).");

        CardSummary? card = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            var brand = cardEl.TryGetProperty("brand", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString()! : "CARD";
            var last = cardEl.TryGetProperty("last_digits", out var ld) && ld.ValueKind == JsonValueKind.String ? ld.GetString()! : "";
            var expiry = cardEl.TryGetProperty("expiry", out var ex) && ex.ValueKind == JsonValueKind.String ? ex.GetString()! : "";
            var name = cardEl.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString() : null;
            if (!string.IsNullOrEmpty(last)) card = new CardSummary(brand, last, expiry, name);
        }

        return new AuthorizationOutcome(orderId, authId, authStatus, expiresAt, card);
    }

    private static bool TryReadFirstAuthorization(JsonElement root, out string authId, out string status, out DateTimeOffset? expiresAt)
    {
        authId = ""; status = ""; expiresAt = null;
        if (!root.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array) return false;
        foreach (var unit in units.EnumerateArray())
        {
            if (!unit.TryGetProperty("payments", out var payments)) continue;
            if (!payments.TryGetProperty("authorizations", out var auths) || auths.ValueKind != JsonValueKind.Array) continue;
            foreach (var auth in auths.EnumerateArray())
            {
                authId = auth.GetProperty("id").GetString()!;
                status = auth.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                expiresAt = ReadDate(auth, "expiration_time");
                return true;
            }
        }
        return false;
    }

    private static bool HasPayerActionLink(JsonElement root)
    {
        if (!root.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array) return false;
        return links.EnumerateArray().Any(l =>
            l.TryGetProperty("rel", out var rel) &&
            string.Equals(rel.GetString(), "payer-action", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryReadBreakdown(JsonElement root, out decimal gross, out decimal fee, out decimal net, out string currency)
    {
        gross = fee = net = 0m; currency = "";
        if (!root.TryGetProperty("seller_receivable_breakdown", out var br)) return false;
        gross = ParseDecimal(br, "gross_amount", "value");
        fee = ParseDecimal(br, "paypal_fee", "value");
        net = ParseDecimal(br, "net_amount", "value");
        if (br.TryGetProperty("gross_amount", out var ga) && ga.TryGetProperty("currency_code", out var cc))
            currency = cc.GetString() ?? "";
        return true;
    }

    private static PayPalTransactionRecord ReadTransaction(JsonElement info)
    {
        string id = info.TryGetProperty("transaction_id", out var tid) ? tid.GetString() ?? "" : "";
        string? status = info.TryGetProperty("transaction_status", out var ts) ? ts.GetString() : null;
        decimal amount = 0m; string currency = "";
        if (info.TryGetProperty("transaction_amount", out var ta))
        {
            amount = ParseDecimal(ta, "value");
            if (ta.TryGetProperty("currency_code", out var cc)) currency = cc.GetString() ?? "";
        }
        decimal? fee = null;
        if (info.TryGetProperty("fee_amount", out var fa) && fa.ValueKind == JsonValueKind.Object)
            fee = ParseDecimal(fa, "value");

        return new PayPalTransactionRecord(
            id, status, amount, currency,
            ReadDate(info, "transaction_initiation_date") ?? ReadDate(info, "transaction_updated_date"),
            info.TryGetProperty("transaction_event_code", out var ec) ? ec.GetString() : null,
            info.TryGetProperty("custom_field", out var cf) ? cf.GetString() : null,
            info.TryGetProperty("invoice_id", out var inv) ? inv.GetString() : null,
            fee);
    }

    private static DateTimeOffset? ReadDate(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return dt;
        return null;
    }

    private static decimal ParseDecimal(JsonElement parent, string objName, string valueName)
    {
        if (parent.TryGetProperty(objName, out var obj) && obj.ValueKind == JsonValueKind.Object)
            return ParseDecimal(obj, valueName);
        return 0m;
    }

    private static decimal ParseDecimal(JsonElement obj, string valueName)
    {
        if (obj.TryGetProperty(valueName, out var v) && v.ValueKind == JsonValueKind.String &&
            decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return d;
        return 0m;
    }

    private static string Fmt(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture);

    private static string LastFour(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : digits;
    }

    private static string FormatReportingDate(DateTimeOffset dt) =>
        dt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    // ---------------- HTTP plumbing ----------------

    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method, string path, object? body, string? idempotencyKey, bool representation, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(idempotencyKey))
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        if (representation)
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, ct);
        return await ReadOrThrowAsync(response, ct);
    }

    private async Task<JsonDocument> SendReportingAsync(string url, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("PayPal-Enforce-ISO8601-Format", "true");
        using var response = await _http.SendAsync(request, ct);
        return await ReadOrThrowAsync(response, ct);
    }

    private async Task<JsonDocument> ReadOrThrowAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            ThrowFromBody((int)response.StatusCode, content);

        if (string.IsNullOrWhiteSpace(content))
            return JsonDocument.Parse("{}");
        return JsonDocument.Parse(content);
    }

    private async Task ThrowFromResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct);
        ThrowFromBody((int)response.StatusCode, content);
    }

    private void ThrowFromBody(int statusCode, string content)
    {
        string? issue = null, description = null, debugId = null, name = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
            var root = doc.RootElement;
            if (root.TryGetProperty("name", out var n)) name = n.GetString();
            if (root.TryGetProperty("message", out var m)) description = m.GetString();
            if (root.TryGetProperty("debug_id", out var d)) debugId = d.GetString();
            if (root.TryGetProperty("details", out var det) && det.ValueKind == JsonValueKind.Array)
            {
                var first = det.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object)
                {
                    if (first.TryGetProperty("issue", out var iss)) issue = iss.GetString();
                    if (first.TryGetProperty("description", out var dd)) description = dd.GetString() ?? description;
                }
            }
        }
        catch (JsonException) { /* non-JSON error body */ }

        issue ??= name;
        var message = description ?? name ?? $"PayPal request failed with status {statusCode}.";
        _logger.LogWarning("PayPal API error {0} (issue {1}, debug_id {2}).", statusCode, issue ?? "?", debugId ?? "?");
        throw new PayPalApiException(statusCode, issue, message, debugId);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            return _accessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
                return _accessToken;

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _http.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                ThrowFromBody((int)response.StatusCode, content);

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            _accessToken = root.GetProperty("access_token").GetString();
            var expiresIn = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var secs) ? secs : 3000;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn - 60));
            return _accessToken!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}

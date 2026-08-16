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
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Talks to the PayPal REST API (Orders v2, Payments v2, Vault v3, Transaction Search v1) exactly as the
/// PayPal plugin's best-practices reference prescribes: OAuth client-credentials with a cached token, a
/// unique <c>PayPal-Request-Id</c> on every POST for idempotency, 429/5xx retry with backoff, and
/// <c>debug_id</c> captured from errors. Card numbers are never logged.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private const string TokenCacheKey = "paypal:access_token";

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<PayPalClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public PayPalClient(HttpClient http, PayPalSettings settings, IMemoryCache cache, IAppLogger<PayPalClient> logger)
    {
        _http = http;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    private string BaseUrl => _settings.ResolvedBaseUrl();

    // ---------------------------------------------------------------------
    // Authorization (hold)
    // ---------------------------------------------------------------------

    public async Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardPaymentDetails card,
        string idempotencyKey, string? customId, string? invoiceId, CancellationToken ct)
    {
        var body = BuildOrderBody(amount, currency, customId, invoiceId, new Dictionary<string, object?>
        {
            ["card"] = BuildCardObject(card)
        });
        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, idempotencyKey, ct);
        return ParseAuthorization(doc!.RootElement);
    }

    public async Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultId,
        string idempotencyKey, string? customId, string? invoiceId, CancellationToken ct)
    {
        var body = BuildOrderBody(amount, currency, customId, invoiceId, new Dictionary<string, object?>
        {
            ["card"] = new Dictionary<string, object?> { ["vault_id"] = vaultId }
        });
        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, idempotencyKey, ct);
        return ParseAuthorization(doc!.RootElement);
    }

    private static Dictionary<string, object?> BuildOrderBody(decimal amount, string currency, string? customId,
        string? invoiceId, Dictionary<string, object?> paymentSource)
    {
        var purchaseUnit = new Dictionary<string, object?>
        {
            ["amount"] = new Dictionary<string, object?>
            {
                ["currency_code"] = currency,
                ["value"] = Money(amount)
            }
        };
        if (!string.IsNullOrEmpty(customId)) purchaseUnit["custom_id"] = customId;
        if (!string.IsNullOrEmpty(invoiceId)) purchaseUnit["invoice_id"] = invoiceId;

        return new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[] { purchaseUnit },
            ["payment_source"] = paymentSource
        };
    }

    private AuthorizationResult ParseAuthorization(JsonElement root)
    {
        var status = GetString(root, "status") ?? "UNKNOWN";
        var payPalOrderId = GetString(root, "id") ?? throw new PayPalException("PayPal did not return an order id.");

        // A card that triggers a browser challenge (3-D Secure step-up) cannot be completed headlessly.
        if (RequiresPayerAction(root, status))
        {
            throw new PayPalException(
                "PayPal requires the shopper to approve this card payment in a browser (3-D Secure challenge). " +
                "This headless integration cannot complete a browser approval round-trip — STOP and report this.",
                422);
        }

        if (!root.TryGetProperty("purchase_units", out var units) || units.GetArrayLength() == 0)
        {
            throw new PayPalException($"PayPal order {payPalOrderId} returned no purchase units (status {status}).", 422);
        }

        var payments = units[0].TryGetProperty("payments", out var pay) ? pay : default;
        if (payments.ValueKind != JsonValueKind.Object ||
            !payments.TryGetProperty("authorizations", out var auths) ||
            auths.GetArrayLength() == 0)
        {
            throw new PayPalException($"PayPal order {payPalOrderId} produced no authorization (status {status}).", 422);
        }

        var auth = auths[0];
        return new AuthorizationResult(
            payPalOrderId,
            GetString(auth, "id") ?? throw new PayPalException("PayPal authorization has no id."),
            GetString(auth, "status") ?? "UNKNOWN",
            GetDate(auth, "expiration_time"));
    }

    private static bool RequiresPayerAction(JsonElement root, string status)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                var rel = GetString(link, "rel");
                if (string.Equals(rel, "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // ---------------------------------------------------------------------
    // Capture (fulfil) + reauthorize + void
    // ---------------------------------------------------------------------

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        var body = new Dictionary<string, object?> { ["final_capture"] = true };
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture",
            body, idempotencyKey, ct, preferRepresentation: true);
        var root = doc!.RootElement;

        var captureId = GetString(root, "id") ?? throw new PayPalException("PayPal capture has no id.");
        var status = GetString(root, "status") ?? "UNKNOWN";

        // The capture response with Prefer: return=representation carries the fee/net breakdown.
        if (TryParseBreakdown(root, out var gross, out var fee, out var net, out var currency))
        {
            return new CaptureResult(captureId, status, gross, fee, net, currency);
        }

        // Fallback: fetch the capture to read the breakdown.
        return await GetCaptureAsync(captureId, ct);
    }

    private async Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/v2/payments/captures/{captureId}", null, null, ct);
        var root = doc!.RootElement;
        var status = GetString(root, "status") ?? "UNKNOWN";
        TryParseBreakdown(root, out var gross, out var fee, out var net, out var currency);
        return new CaptureResult(captureId, status, gross, fee, net, currency);
    }

    private static bool TryParseBreakdown(JsonElement root, out decimal gross, out decimal fee, out decimal net, out string currency)
    {
        gross = fee = net = 0m;
        currency = string.Empty;
        if (!root.TryGetProperty("seller_receivable_breakdown", out var b) || b.ValueKind != JsonValueKind.Object)
        {
            if (root.TryGetProperty("amount", out var amt))
            {
                gross = ParseMoney(amt, out currency);
                net = gross;
            }
            return false;
        }
        if (b.TryGetProperty("gross_amount", out var g)) gross = ParseMoney(g, out currency);
        if (b.TryGetProperty("paypal_fee", out var f)) fee = ParseMoney(f, out _);
        if (b.TryGetProperty("net_amount", out var n)) net = ParseMoney(n, out _);
        return true;
    }

    public async Task<AuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, ct);
        var root = doc!.RootElement;
        return new AuthorizationInfo(GetString(root, "status") ?? "UNKNOWN", GetDate(root, "expiration_time"));
    }

    public async Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = new Dictionary<string, object?> { ["currency_code"] = currency, ["value"] = Money(amount) }
        };
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body, Guid.NewGuid().ToString(), ct, preferRepresentation: true);
        var root = doc!.RootElement;
        return new ReauthorizeResult(
            GetString(root, "id") ?? authorizationId,
            GetString(root, "status") ?? "UNKNOWN",
            GetDate(root, "expiration_time"));
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void",
            null, Guid.NewGuid().ToString(), ct, allowEmpty: true);
    }

    // ---------------------------------------------------------------------
    // Refund
    // ---------------------------------------------------------------------

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, string? note, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>();
        if (amount.HasValue)
        {
            body["amount"] = new Dictionary<string, object?> { ["currency_code"] = currency, ["value"] = Money(amount.Value) };
        }
        if (!string.IsNullOrEmpty(note))
        {
            body["note_to_payer"] = note;
        }

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund",
            body, idempotencyKey, ct, preferRepresentation: true);
        var root = doc!.RootElement;

        var refundId = GetString(root, "id") ?? throw new PayPalException("PayPal refund has no id.");
        var status = GetString(root, "status") ?? "UNKNOWN";
        var value = amount ?? 0m;
        if (root.TryGetProperty("amount", out var amt))
        {
            value = ParseMoney(amt, out _);
        }
        return new RefundResult(refundId, status, value, currency);
    }

    // ---------------------------------------------------------------------
    // Vault (save card)
    // ---------------------------------------------------------------------

    public async Task<VaultedCardResult> VaultCardAsync(CardPaymentDetails card, string? existingCustomerId,
        string idempotencyKey, CancellationToken ct)
    {
        var setupBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = BuildCardObject(card) }
        };
        if (!string.IsNullOrEmpty(existingCustomerId))
        {
            setupBody["customer"] = new Dictionary<string, object?> { ["id"] = existingCustomerId };
        }

        using var setupDoc = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody, idempotencyKey, ct);
        var setupId = GetString(setupDoc!.RootElement, "id")
            ?? throw new PayPalException("PayPal did not return a setup token id.");

        var tokenBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?> { ["id"] = setupId, ["type"] = "SETUP_TOKEN" }
            }
        };
        using var tokenDoc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenBody,
            Guid.NewGuid().ToString(), ct);
        var root = tokenDoc!.RootElement;

        var paymentTokenId = GetString(root, "id")
            ?? throw new PayPalException("PayPal did not return a payment token id.");
        var customerId = root.TryGetProperty("customer", out var cust) ? GetString(cust, "id") : null;

        string brand = "CARD", last4 = "****", expiry = string.Empty, name = string.Empty;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            brand = GetString(cardEl, "brand") ?? brand;
            last4 = GetString(cardEl, "last_digits") ?? last4;
            expiry = GetString(cardEl, "expiry") ?? expiry;
            name = GetString(cardEl, "name") ?? name;
        }

        return new VaultedCardResult(paymentTokenId, customerId ?? string.Empty, brand, last4, expiry,
            string.IsNullOrEmpty(name) ? null : name);
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken ct)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{paymentTokenId}",
            null, null, ct, allowEmpty: true, treatNotFoundAsSuccess: true);
    }

    // ---------------------------------------------------------------------
    // Transaction search (reconciliation)
    // ---------------------------------------------------------------------

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<PayPalTransaction>();

        // The Transaction Search API accepts at most a 31-day window, so chunk the whole range.
        var windowStart = from.ToUniversalTime();
        var end = to.ToUniversalTime();
        while (windowStart < end)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > end) windowEnd = end;

            await CollectWindowAsync(windowStart, windowEnd, results, ct);
            windowStart = windowEnd;
        }

        return results;
    }

    private async Task CollectWindowAsync(DateTimeOffset start, DateTimeOffset end, List<PayPalTransaction> results, CancellationToken ct)
    {
        int page = 1, totalPages = 1;
        do
        {
            var query = $"?start_date={Iso(start)}&end_date={Iso(end)}&fields=transaction_info&page_size=500&page={page}";
            using var doc = await SendAsync(HttpMethod.Get, "/v1/reporting/transactions" + query, null, null, ct);
            var root = doc!.RootElement;

            if (root.TryGetProperty("total_pages", out var tp) && tp.ValueKind == JsonValueKind.Number)
            {
                totalPages = tp.GetInt32();
            }

            if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in details.EnumerateArray())
                {
                    if (d.TryGetProperty("transaction_info", out var info))
                    {
                        results.Add(ParseTransaction(info));
                    }
                }
            }
            page++;
        }
        while (page <= totalPages);
    }

    private static PayPalTransaction ParseTransaction(JsonElement info)
    {
        var amount = 0m; var currency = string.Empty; var fee = 0m;
        if (info.TryGetProperty("transaction_amount", out var amt)) amount = ParseMoney(amt, out currency);
        if (info.TryGetProperty("fee_amount", out var f)) fee = ParseMoney(f, out _);

        return new PayPalTransaction(
            GetString(info, "transaction_id") ?? string.Empty,
            GetString(info, "paypal_reference_id"),
            GetString(info, "transaction_status") ?? string.Empty,
            amount,
            currency,
            fee,
            GetDate(info, "transaction_initiation_date") ?? DateTimeOffset.MinValue,
            GetString(info, "invoice_id"),
            GetString(info, "custom_field"),
            GetString(info, "transaction_subject"),
            GetString(info, "transaction_event_code"));
    }

    // ---------------------------------------------------------------------
    // OAuth + HTTP plumbing
    // ---------------------------------------------------------------------

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue<string>(TokenCacheKey, out var cached) && !string.IsNullOrEmpty(cached))
        {
            return cached!;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/oauth2/token");
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        req.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });

        using var resp = await _http.SendAsync(req, ct);
        var content = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new PayPalException($"PayPal token request failed ({(int)resp.StatusCode}). Check PayPal:ClientId/ClientSecret.",
                (int)resp.StatusCode);
        }

        using var doc = JsonDocument.Parse(content);
        var token = GetString(doc.RootElement, "access_token")
            ?? throw new PayPalException("PayPal token response had no access_token.");
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number
            ? e.GetInt32() : 3000;

        _cache.Set(TokenCacheKey, token, TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60)));
        return token;
    }

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken ct, bool preferRepresentation = false, bool allowEmpty = false, bool treatNotFoundAsSuccess = false)
    {
        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            var token = await GetAccessTokenAsync(ct);
            using var req = new HttpRequestMessage(method, BaseUrl + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (!string.IsNullOrEmpty(requestId))
            {
                req.Headers.Add("PayPal-Request-Id", requestId);
            }
            if (preferRepresentation)
            {
                req.Headers.Add("Prefer", "return=representation");
            }
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(req, ct);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                await BackoffAsync(attempt, null, ct);
                _logger.LogWarning($"PayPal {method} {path} transport error (attempt {attempt}): {ex.Message}");
                continue;
            }

            try
            {
                if (treatNotFoundAsSuccess && resp.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                // Retry transient failures with backoff.
                if ((resp.StatusCode == (HttpStatusCode)429 || (int)resp.StatusCode >= 500) && attempt < maxAttempts)
                {
                    await BackoffAsync(attempt, resp, ct);
                    _logger.LogWarning($"PayPal {method} {path} returned {(int)resp.StatusCode} (attempt {attempt}); retrying.");
                    continue;
                }

                var content = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                {
                    throw BuildError(method, path, resp, content);
                }

                if (allowEmpty || string.IsNullOrWhiteSpace(content))
                {
                    return null;
                }
                return JsonDocument.Parse(content);
            }
            finally
            {
                resp.Dispose();
            }
        }
    }

    private static async Task BackoffAsync(int attempt, HttpResponseMessage? resp, CancellationToken ct)
    {
        var delay = TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
        if (resp?.Headers.RetryAfter?.Delta is TimeSpan retryAfter && retryAfter > delay)
        {
            delay = retryAfter;
        }
        await Task.Delay(delay, ct);
    }

    private PayPalException BuildError(HttpMethod method, string path, HttpResponseMessage resp, string content)
    {
        string? debugId = null;
        var message = new StringBuilder();
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            debugId = GetString(root, "debug_id");
            var name = GetString(root, "name");
            var desc = GetString(root, "message");
            if (!string.IsNullOrEmpty(name)) message.Append(name);
            if (!string.IsNullOrEmpty(desc)) message.Append(message.Length > 0 ? $": {desc}" : desc);

            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                var issues = details.EnumerateArray()
                    .Select(d => GetString(d, "issue") ?? GetString(d, "description"))
                    .Where(s => !string.IsNullOrEmpty(s));
                var joined = string.Join("; ", issues);
                if (!string.IsNullOrEmpty(joined)) message.Append($" [{joined}]");
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body — fall through to a generic message.
        }

        if (message.Length == 0)
        {
            message.Append($"PayPal {method} {path} failed ({(int)resp.StatusCode}).");
        }

        _logger.LogWarning($"PayPal {method} {path} -> {(int)resp.StatusCode} debug_id={debugId} :: {message}");
        return new PayPalException(message.ToString(), (int)resp.StatusCode, debugId);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static Dictionary<string, object?> BuildCardObject(CardPaymentDetails card)
    {
        var obj = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry
        };
        if (!string.IsNullOrEmpty(card.SecurityCode)) obj["security_code"] = card.SecurityCode;
        if (!string.IsNullOrEmpty(card.Name)) obj["name"] = card.Name;

        if (card.BillingAddress is { } addr)
        {
            var billing = new Dictionary<string, object?>();
            if (!string.IsNullOrEmpty(addr.Line1)) billing["address_line_1"] = addr.Line1;
            if (!string.IsNullOrEmpty(addr.Line2)) billing["address_line_2"] = addr.Line2;
            if (!string.IsNullOrEmpty(addr.City)) billing["admin_area_2"] = addr.City;
            if (!string.IsNullOrEmpty(addr.State)) billing["admin_area_1"] = addr.State;
            if (!string.IsNullOrEmpty(addr.PostalCode)) billing["postal_code"] = addr.PostalCode;
            if (!string.IsNullOrEmpty(addr.CountryCode)) billing["country_code"] = addr.CountryCode;
            if (billing.Count > 0) obj["billing_address"] = billing;
        }
        return obj;
    }

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(JsonElement moneyEl, out string currency)
    {
        currency = GetString(moneyEl, "currency_code") ?? string.Empty;
        var value = GetString(moneyEl, "value");
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    private static string Iso(DateTimeOffset dt) =>
        dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string? GetString(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static DateTimeOffset? GetDate(JsonElement el, string prop)
    {
        var s = GetString(el, prop);
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
            ? dt
            : null;
    }
}

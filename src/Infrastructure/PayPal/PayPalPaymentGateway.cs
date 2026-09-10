using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The PayPal REST adapter. Talks OAuth2, Orders v2, Payments v2, Vault v3 and Transaction Search v1.
/// All PayPal-specific request/response shapes are contained here; the rest of the app depends only on
/// <see cref="IPayPalPaymentGateway"/> and the domain-level result records.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    private const string TokenCacheKeyPrefix = "paypal-access-token::";

    // Transaction Search allows at most a 31-day window per request.
    private static readonly TimeSpan MaxReportingWindow = TimeSpan.FromDays(31);

    public PayPalPaymentGateway(HttpClient httpClient, IOptions<PayPalSettings> settings,
        IMemoryCache cache, ILogger<PayPalPaymentGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    private string CurrencyValue(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    // --- Authentication -----------------------------------------------------------------------

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var cacheKey = TokenCacheKeyPrefix + _settings.ClientId;
        if (_cache.TryGetValue<string>(cacheKey, out var cached) && !string.IsNullOrEmpty(cached))
        {
            return cached!;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildException("Failed to obtain a PayPal access token", (int)response.StatusCode, body, response);
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var token = root.GetProperty("access_token").GetString()!;
        var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3000;

        // Refresh a little before the token actually expires.
        var lifetime = TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60));
        _cache.Set(cacheKey, token, lifetime);
        return token;
    }

    // --- Request plumbing ---------------------------------------------------------------------

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string path, object? body,
        string? idempotencyKey, CancellationToken cancellationToken, bool preferRepresentation = true)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (preferRepresentation)
        {
            request.Headers.Add("Prefer", "return=representation");
        }
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.Add("PayPal-Request-Id", idempotencyKey);
        }
        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException($"PayPal {method} {path} failed", (int)response.StatusCode, responseBody, response);
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }
        return JsonDocument.Parse(responseBody);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private PayPalGatewayException BuildException(string context, int statusCode, string body, HttpResponseMessage response)
    {
        string? name = null;
        string? message = null;
        string? debugId = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("name", out var n)) name = n.GetString();
                if (root.TryGetProperty("message", out var m)) message = m.GetString();
                if (root.TryGetProperty("debug_id", out var d)) debugId = d.GetString();
                if (root.TryGetProperty("error_description", out var ed) && message == null) message = ed.GetString();

                // Surface the first field-level issue, which is far more actionable than the generic message.
                if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        var issue = detail.TryGetProperty("issue", out var iss) ? iss.GetString() : null;
                        var desc = detail.TryGetProperty("description", out var de) ? de.GetString() : null;
                        if (!string.IsNullOrEmpty(issue))
                        {
                            name = string.IsNullOrEmpty(name) ? issue : $"{name}/{issue}";
                            if (!string.IsNullOrEmpty(desc)) message = desc;
                        }
                        break;
                    }
                }
            }
        }
        catch (JsonException) { /* non-JSON error body */ }

        if (debugId == null && response.Headers.TryGetValues("PayPal-Debug-Id", out var ids))
        {
            foreach (var id in ids) { debugId = id; break; }
        }

        var full = $"{context}: {statusCode} {name} {message} (debug_id={debugId})";
        _logger.LogWarning("PayPal error: {Context} status={Status} name={Name} debugId={DebugId}", context, statusCode, name, debugId);
        return new PayPalGatewayException(full, statusCode, name, debugId);
    }

    // --- Orders / authorize -------------------------------------------------------------------

    public async Task<AuthorizationResult> AuthorizeAsync(decimal amount, string currency, string invoiceId,
        string customId, CardDetails? card, string? vaultId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object cardSource = vaultId != null
            ? new { vault_id = vaultId }
            : BuildCardObject(card!);

        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    invoice_id = invoiceId,
                    custom_id = customId,
                    amount = new { currency_code = currency, value = CurrencyValue(amount) }
                }
            },
            payment_source = new { card = cardSource }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, idempotencyKey, cancellationToken);
        var root = doc!.RootElement;

        var orderStatus = root.TryGetProperty("status", out var s) ? s.GetString() : null;
        var payPalOrderId = root.GetProperty("id").GetString()!;

        // A challenge (e.g. 3-D Secure) surfaces as PAYER_ACTION_REQUIRED plus a payer-action link.
        if (string.Equals(orderStatus, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || HasLink(root, "payer-action"))
        {
            throw new PayPalChallengeRequiredException(
                $"PayPal requires buyer approval (status {orderStatus}) for order {payPalOrderId}; this browser-free integration cannot complete it.");
        }

        var authorization = FindFirstAuthorization(root);
        if (authorization == null)
        {
            throw new PayPalGatewayException(
                $"PayPal did not return an authorization for order {payPalOrderId} (status {orderStatus}).",
                (int)HttpStatusCode.BadGateway, orderStatus, null);
        }

        var authValue = authorization.Value;
        var authId = authValue.GetProperty("id").GetString()!;
        var authStatus = authValue.TryGetProperty("status", out var a) ? a.GetString() ?? "UNKNOWN" : "UNKNOWN";

        if (string.Equals(authStatus, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalGatewayException(
                $"The card was declined by PayPal (authorization {authId} is DENIED).",
                (int)HttpStatusCode.PaymentRequired, "INSTRUMENT_DECLINED", null);
        }

        var expiresAt = ReadDateTime(authValue, "expiration_time");
        return new AuthorizationResult(payPalOrderId, authId, authStatus, expiresAt);
    }

    private object BuildCardObject(CardDetails card)
    {
        object? billing = null;
        if (card.BillingAddress != null)
        {
            var b = card.BillingAddress;
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
            expiry = card.Expiry,
            security_code = card.SecurityCode,
            name = card.Name,
            billing_address = billing
        };
    }

    // --- Payments: capture / reauthorize / void / refund --------------------------------------

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new { final_capture = true };
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, idempotencyKey, cancellationToken);
        var root = doc!.RootElement;

        var captureId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "UNKNOWN" : "UNKNOWN";

        decimal gross = ReadAmount(root, "amount");
        decimal fee = 0m, net = gross;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            gross = ReadAmount(breakdown, "gross_amount");
            fee = ReadAmount(breakdown, "paypal_fee");
            net = ReadAmount(breakdown, "net_amount");
        }

        return new CaptureResult(captureId, status, gross, fee, net);
    }

    public async Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken cancellationToken = default)
    {
        var body = new { amount = new { currency_code = currency, value = CurrencyValue(amount) } };
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, null, cancellationToken);
        var root = doc!.RootElement;

        var newAuthId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "UNKNOWN" : "UNKNOWN";
        var expiresAt = ReadDateTime(root, "expiration_time");
        return new ReauthorizationResult(newAuthId, status, expiresAt);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, null, cancellationToken);
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object? body = amount.HasValue
            ? new { amount = new { currency_code = currency, value = CurrencyValue(amount.Value) } }
            : new { };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, idempotencyKey, cancellationToken);
        var root = doc!.RootElement;

        var refundId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "UNKNOWN" : "UNKNOWN";
        var refunded = root.TryGetProperty("amount", out _) ? ReadAmount(root, "amount") : (amount ?? 0m);
        return new RefundResult(refundId, status, refunded);
    }

    // --- Vault ---------------------------------------------------------------------------------

    public async Task<VaultedCard> VaultCardAsync(CardDetails card, string? customerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object body = customerId != null
            ? new { customer = new { id = customerId }, payment_source = new { card = BuildCardObject(card) } }
            : new { payment_source = new { card = BuildCardObject(card) } };

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, idempotencyKey, cancellationToken);
        var root = doc!.RootElement;

        var vaultId = root.GetProperty("id").GetString()!;
        string? returnedCustomerId = null;
        if (root.TryGetProperty("customer", out var customer) && customer.TryGetProperty("id", out var cid))
        {
            returnedCustomerId = cid.GetString();
        }

        string? brand = null, last4 = null, expiry = null, name = null;
        if (root.TryGetProperty("payment_source", out var source) && source.TryGetProperty("card", out var c))
        {
            if (c.TryGetProperty("brand", out var b)) brand = b.GetString();
            if (c.TryGetProperty("last_digits", out var l)) last4 = l.GetString();
            if (c.TryGetProperty("expiry", out var e)) expiry = e.GetString();
            if (c.TryGetProperty("name", out var n)) name = n.GetString();
        }

        return new VaultedCard(vaultId, returnedCustomerId, brand, last4, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, cancellationToken, preferRepresentation: false);
    }

    // --- Transaction Search (reconciliation) --------------------------------------------------

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();
        // Adjacent windows share a boundary instant, so guard against counting a boundary transaction twice.
        var seen = new HashSet<string>();

        // Chunk the range into windows of at most 31 days, then page through every page of each window.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxReportingWindow;
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            int totalPages;
            do
            {
                var query = $"?start_date={Iso(windowStart)}&end_date={Iso(windowEnd)}&fields=all&page_size=500&page={page}&total_required=true";
                using var doc = await SendAsync(HttpMethod.Get, "/v1/reporting/transactions" + query, null, null, cancellationToken, preferRepresentation: false);
                var root = doc!.RootElement;

                totalPages = root.TryGetProperty("total_pages", out var tp) ? tp.GetInt32() : 1;

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (!detail.TryGetProperty("transaction_info", out var info)) continue;
                        var transaction = ReadTransaction(info);
                        if (string.IsNullOrEmpty(transaction.TransactionId) || seen.Add(transaction.TransactionId))
                        {
                            results.Add(transaction);
                        }
                    }
                }
                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd == to ? to : windowEnd;
            if (windowEnd == to) break;
        }

        return results;
    }

    private static PayPalTransaction ReadTransaction(JsonElement info)
    {
        var txnId = info.TryGetProperty("transaction_id", out var t) ? t.GetString() ?? "" : "";
        var invoiceId = info.TryGetProperty("invoice_id", out var iv) ? iv.GetString() : null;
        var customField = info.TryGetProperty("custom_field", out var cf) ? cf.GetString() : null;
        var status = info.TryGetProperty("transaction_status", out var st) ? st.GetString() ?? "" : "";
        var eventCode = info.TryGetProperty("transaction_event_code", out var ec) ? ec.GetString() : null;

        decimal amount = 0m;
        string currency = "";
        if (info.TryGetProperty("transaction_amount", out var amt))
        {
            if (amt.TryGetProperty("value", out var v) && decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                amount = parsed;
            if (amt.TryGetProperty("currency_code", out var cc)) currency = cc.GetString() ?? "";
        }

        DateTimeOffset date = default;
        if (info.TryGetProperty("transaction_initiation_date", out var d) &&
            DateTimeOffset.TryParse(d.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate))
        {
            date = parsedDate;
        }

        return new PayPalTransaction(txnId, invoiceId, customField, amount, currency, status, date, eventCode);
    }

    // --- JSON helpers -------------------------------------------------------------------------

    private static string Iso(DateTimeOffset value) =>
        Uri.EscapeDataString(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

    private static bool HasLink(JsonElement root, string rel)
    {
        if (!root.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array) return false;
        foreach (var link in links.EnumerateArray())
        {
            if (link.TryGetProperty("rel", out var r) && string.Equals(r.GetString(), rel, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static JsonElement? FindFirstAuthorization(JsonElement orderRoot)
    {
        if (!orderRoot.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array) return null;
        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments) &&
                payments.TryGetProperty("authorizations", out var auths) &&
                auths.ValueKind == JsonValueKind.Array)
            {
                foreach (var auth in auths.EnumerateArray())
                {
                    return auth;
                }
            }
        }
        return null;
    }

    private static decimal ReadAmount(JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var money) && money.TryGetProperty("value", out var v)
            && decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        return 0m;
    }

    private static DateTimeOffset? ReadDateTime(JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var v) &&
            DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }
        return null;
    }
}

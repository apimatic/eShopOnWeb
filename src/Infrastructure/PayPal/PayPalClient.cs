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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The concrete PayPal REST client — the only place that knows PayPal's wire format. Handles OAuth token
/// caching, idempotency headers, retry/backoff, and error parsing (debug id + issue names).
/// </summary>
public class PayPalClient : IPayPalPaymentGateway
{
    private const int MaxRetries = 3;

    private const string TokenCacheKey = "paypal:access_token";

    // Single-flight guard shared across the transient typed-client instances so we never stampede the token endpoint.
    private static readonly SemaphoreSlim TokenLock = new(1, 1);

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalClient> _logger;
    private readonly IMemoryCache _cache;

    public PayPalClient(HttpClient http, IOptions<PayPalSettings> settings, IAppLogger<PayPalClient> logger, IMemoryCache cache)
    {
        _settings = settings.Value;
        _http = http;
        _logger = logger;
        _cache = cache;

        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            _logger.LogWarning("PayPal ClientId/ClientSecret are not configured. Set them via the PayPal: configuration section (user-secrets).");
        }
    }

    public string Currency => string.IsNullOrWhiteSpace(_settings.Currency) ? "USD" : _settings.Currency;

    private string BaseUrl => _settings.ResolveBaseUrl();

    // ---------------------------------------------------------------------------------------------
    // Orders
    // ---------------------------------------------------------------------------------------------

    public async Task<string> CreateOrderForAuthorizationAsync(decimal amount, string currency, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    invoice_id = invoiceId,
                    custom_id = invoiceId,
                    amount = new { currency_code = currency, value = PayPalMoney.Format(amount, currency) }
                }
            }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, idempotencyKey, prefer: "return=minimal", cancellationToken: cancellationToken);
        var id = doc!.RootElement.GetProperty("id").GetString();
        if (string.IsNullOrEmpty(id))
        {
            throw new PayPalApiException("PayPal did not return an order id.", 502, null, Array.Empty<string>());
        }
        return id!;
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string payPalOrderId, PayPalPaymentSource source, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["payment_source"] = BuildCardPaymentSource(source) };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize", body, idempotencyKey, prefer: "return=representation", cancellationToken: cancellationToken);
        var root = doc!.RootElement;

        EnsureNoBuyerChallenge(root, "authorize this card");

        var authorization = FindFirst(root, "purchase_units", "payments", "authorizations");
        if (authorization is null)
        {
            var status = root.TryGetProperty("status", out var s) ? s.GetString() : "UNKNOWN";
            throw new PayPalApiException($"PayPal did not return an authorization (order status {status}).", 502, TryGetDebugId(root), Array.Empty<string>());
        }

        var auth = authorization.Value;
        var authId = auth.GetProperty("id").GetString()!;
        var authStatus = auth.TryGetProperty("status", out var st) ? st.GetString() ?? "CREATED" : "CREATED";
        DateTimeOffset? expiresAt = auth.TryGetProperty("expiration_time", out var exp) && exp.ValueKind == JsonValueKind.String
            ? ParseDate(exp.GetString())
            : null;

        var instrument = DescribeCard(root);
        return new PayPalAuthorizationResult(payPalOrderId, authId, authStatus, expiresAt, instrument);
    }

    // ---------------------------------------------------------------------------------------------
    // Payments (capture / reauthorize / void / refund)
    // ---------------------------------------------------------------------------------------------

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = new { currency_code = currency, value = PayPalMoney.Format(amount, currency) },
            invoice_id = invoiceId,
            final_capture = true
        };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, idempotencyKey, prefer: "return=representation", cancellationToken: cancellationToken);
        var root = doc!.RootElement;

        var captureId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "COMPLETED" : "COMPLETED";
        var gross = amount;
        decimal? fee = null;
        decimal? net = null;
        var currencyCode = currency;

        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown) && breakdown.ValueKind == JsonValueKind.Object)
        {
            gross = MoneyValue(breakdown, "gross_amount") ?? gross;
            fee = MoneyValue(breakdown, "paypal_fee");
            net = MoneyValue(breakdown, "net_amount");
            currencyCode = MoneyCurrency(breakdown, "gross_amount") ?? currencyCode;
        }

        return new PayPalCaptureResult(captureId, status, gross, fee, net, currencyCode);
    }

    public async Task<PayPalReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new { amount = new { currency_code = currency, value = PayPalMoney.Format(amount, currency) } };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, idempotencyKey, prefer: "return=representation", cancellationToken: cancellationToken);
        var root = doc!.RootElement;

        var newAuthId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? authorizationId : authorizationId;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "CREATED" : "CREATED";
        DateTimeOffset? expiresAt = root.TryGetProperty("expiration_time", out var exp) && exp.ValueKind == JsonValueKind.String
            ? ParseDate(exp.GetString())
            : null;

        return new PayPalReauthorizeResult(newAuthId, status, expiresAt);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", body: null, idempotencyKey: null, prefer: "return=minimal", cancellationToken: cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object? body = amount is null
            ? null // full refund = empty body
            : new { amount = new { currency_code = currency, value = PayPalMoney.Format(amount.Value, currency) } };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, idempotencyKey, prefer: "return=representation", cancellationToken: cancellationToken);
        var root = doc!.RootElement;

        var refundId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "COMPLETED" : "COMPLETED";
        var refundedAmount = MoneyValue(root, "amount") ?? amount ?? 0m;
        var currencyCode = MoneyCurrency(root, "amount") ?? currency;

        return new PayPalRefundResult(refundId, status, refundedAmount, currencyCode);
    }

    // ---------------------------------------------------------------------------------------------
    // Vault
    // ---------------------------------------------------------------------------------------------

    public async Task<PayPalVaultCardResult> VaultCardAsync(PayPalCardDetails card, string? existingCustomerId, string merchantCustomerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var cardObject = BuildRawCard(card);

        var body = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = cardObject }
        };

        if (!string.IsNullOrEmpty(existingCustomerId))
        {
            body["customer"] = new Dictionary<string, object?> { ["id"] = existingCustomerId };
        }
        else if (!string.IsNullOrEmpty(merchantCustomerId))
        {
            body["customer"] = new Dictionary<string, object?> { ["merchant_customer_id"] = merchantCustomerId };
        }

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, idempotencyKey, prefer: "return=representation", cancellationToken: cancellationToken);
        var root = doc!.RootElement;

        EnsureNoBuyerChallenge(root, "vault this card");

        var vaultId = root.GetProperty("id").GetString()!;
        var customerId = root.TryGetProperty("customer", out var cust) && cust.TryGetProperty("id", out var cid) ? cid.GetString() : null;

        string? brand = null, lastDigits = null, expiry = null, name = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var respCard))
        {
            brand = GetString(respCard, "brand");
            lastDigits = GetString(respCard, "last_digits");
            expiry = GetString(respCard, "expiry");
            name = GetString(respCard, "name");
        }

        return new PayPalVaultCardResult(vaultId, customerId ?? existingCustomerId ?? merchantCustomerId, brand, lastDigits, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", body: null, idempotencyKey: null, prefer: null, cancellationToken: cancellationToken);
    }

    // ---------------------------------------------------------------------------------------------
    // Transaction search (reconciliation)
    // ---------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();

        // PayPal's transaction search allows at most a 31-day window per request, so cover the whole
        // range by walking it in <= 31-day chunks, and paginate each chunk to the last page.
        var chunkStart = from;
        while (chunkStart < to)
        {
            var chunkEnd = chunkStart.AddDays(31);
            if (chunkEnd > to) chunkEnd = to;

            var page = 1;
            int totalPages;
            do
            {
                var query = "/v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(FormatSearchDate(chunkStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatSearchDate(chunkEnd))}" +
                    "&fields=all&balance_affecting_records_only=N&page_size=100" +
                    $"&page={page}";

                using var doc = await SendAsync(HttpMethod.Get, query, body: null, idempotencyKey: null, prefer: null, cancellationToken: cancellationToken);
                var root = doc!.RootElement;

                totalPages = root.TryGetProperty("total_pages", out var tp) && tp.ValueKind == JsonValueKind.Number ? tp.GetInt32() : 1;

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (detail.TryGetProperty("transaction_info", out var info))
                        {
                            results.Add(ParseTransaction(info));
                        }
                    }
                }

                page++;
            }
            while (page <= totalPages);

            chunkStart = chunkEnd;
        }

        return results;
    }

    private static PayPalTransaction ParseTransaction(JsonElement info)
    {
        var transactionId = GetString(info, "transaction_id") ?? string.Empty;
        var eventCode = GetString(info, "transaction_event_code");
        var status = GetString(info, "transaction_status");
        var amount = MoneyValue(info, "transaction_amount") ?? 0m;
        var currency = MoneyCurrency(info, "transaction_amount") ?? string.Empty;
        var fee = MoneyValue(info, "fee_amount");
        var date = ParseDate(GetString(info, "transaction_initiation_date") ?? GetString(info, "transaction_updated_date"));
        var invoiceId = GetString(info, "invoice_id");
        var customField = GetString(info, "custom_field");

        return new PayPalTransaction(transactionId, eventCode, status, amount, fee, currency, date, invoiceId, customField);
    }

    // ---------------------------------------------------------------------------------------------
    // Request payload builders
    // ---------------------------------------------------------------------------------------------

    private static Dictionary<string, object?> BuildCardPaymentSource(PayPalPaymentSource source)
    {
        return source switch
        {
            VaultedCardPaymentSource vaulted => new Dictionary<string, object?>
            {
                ["card"] = new Dictionary<string, object?> { ["vault_id"] = vaulted.VaultId }
            },
            CardPaymentSource card => new Dictionary<string, object?>
            {
                ["card"] = BuildRawCard(card.Card)
            },
            _ => throw new PaymentException(PaymentErrorReason.Validation, "A card or a saved card must be supplied to pay.")
        };
    }

    private static Dictionary<string, object?> BuildRawCard(PayPalCardDetails card)
    {
        var result = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode
        };

        if (!string.IsNullOrWhiteSpace(card.Name))
        {
            result["name"] = card.Name;
        }

        var address = new Dictionary<string, object?>
        {
            ["country_code"] = string.IsNullOrWhiteSpace(card.CountryCode) ? "US" : card.CountryCode
        };
        if (!string.IsNullOrWhiteSpace(card.BillingAddressLine1)) address["address_line_1"] = card.BillingAddressLine1;
        if (!string.IsNullOrWhiteSpace(card.BillingAddressLine2)) address["address_line_2"] = card.BillingAddressLine2;
        if (!string.IsNullOrWhiteSpace(card.AdminArea1)) address["admin_area_1"] = card.AdminArea1;
        if (!string.IsNullOrWhiteSpace(card.AdminArea2)) address["admin_area_2"] = card.AdminArea2;
        if (!string.IsNullOrWhiteSpace(card.PostalCode)) address["postal_code"] = card.PostalCode;
        result["billing_address"] = address;

        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // HTTP plumbing
    // ---------------------------------------------------------------------------------------------

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string pathOrQuery, object? body, string? idempotencyKey, string? prefer, CancellationToken cancellationToken)
    {
        var url = pathOrQuery.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? pathOrQuery : BaseUrl + pathOrQuery;

        for (var attempt = 1; ; attempt++)
        {
            var token = await GetAccessTokenAsync(cancellationToken);

            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
            }
            if (!string.IsNullOrEmpty(prefer))
            {
                request.Headers.TryAddWithoutValidation("Prefer", prefer);
            }
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                await BackoffAsync(attempt, cancellationToken);
                _logger.LogWarning("PayPal request to {0} failed ({1}); retrying (attempt {2}).", url, ex.Message, attempt + 1);
                continue;
            }

            using (response)
            {
                var debugId = response.Headers.TryGetValues("Paypal-Debug-Id", out var ids) ? ids.FirstOrDefault() : null;
                var payload = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(payload))
                    {
                        return JsonDocument.Parse("{}");
                    }
                    return JsonDocument.Parse(payload);
                }

                // Retry transient failures (idempotency headers make money POSTs safe to retry).
                if ((response.StatusCode == (HttpStatusCode)429 || (int)response.StatusCode >= 500) && attempt < MaxRetries)
                {
                    await BackoffAsync(attempt, cancellationToken);
                    _logger.LogWarning("PayPal {0} {1} returned {2} (debug id {3}); retrying (attempt {4}).", method, url, (int)response.StatusCode, debugId ?? "n/a", attempt + 1);
                    continue;
                }

                throw BuildApiException((int)response.StatusCode, payload, debugId);
            }
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<string>(TokenCacheKey, out var cached) && !string.IsNullOrEmpty(cached))
        {
            return cached!;
        }

        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue<string>(TokenCacheKey, out var cachedAfterLock) && !string.IsNullOrEmpty(cachedAfterLock))
            {
                return cachedAfterLock!;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });

            using var response = await _http.SendAsync(request, cancellationToken);
            var debugId = response.Headers.TryGetValues("Paypal-Debug-Id", out var ids) ? ids.FirstOrDefault() : null;
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw BuildApiException((int)response.StatusCode, payload, debugId,
                    fallbackMessage: "Failed to obtain a PayPal access token. Check PayPal:ClientId / PayPal:ClientSecret / PayPal:Environment.");
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var accessToken = root.GetProperty("access_token").GetString()!;
            var expiresIn = root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 3000;
            // Refresh a minute early to avoid using a token that expires mid-request.
            _cache.Set(TokenCacheKey, accessToken, TimeSpan.FromSeconds(Math.Max(30, expiresIn - 60)));
            return accessToken;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private static async Task BackoffAsync(int attempt, CancellationToken cancellationToken)
    {
        // Exponential backoff with a fixed jitter component (no wall-clock randomness needed for correctness).
        var delayMs = (int)(200 * Math.Pow(3, attempt - 1)) + (attempt * 50);
        await Task.Delay(delayMs, cancellationToken);
    }

    private static PayPalApiException BuildApiException(int statusCode, string payload, string? debugId, string? fallbackMessage = null)
    {
        var issues = new List<string>();
        string message = fallbackMessage ?? "PayPal request failed.";

        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;

                if (root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                {
                    issues.Add(name.GetString()!);
                }
                if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                {
                    issues.Add(err.GetString()!);
                }
                if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                {
                    message = msg.GetString()!;
                }
                else if (root.TryGetProperty("error_description", out var ed) && ed.ValueKind == JsonValueKind.String)
                {
                    message = ed.GetString()!;
                }
                if (root.TryGetProperty("debug_id", out var did) && did.ValueKind == JsonValueKind.String)
                {
                    debugId ??= did.GetString();
                }
                if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (detail.TryGetProperty("issue", out var issue) && issue.ValueKind == JsonValueKind.String)
                        {
                            issues.Add(issue.GetString()!);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Non-JSON error body; keep the fallback message.
            }
        }

        var issueText = issues.Count > 0 ? $" [{string.Join(", ", issues)}]" : string.Empty;
        return new PayPalApiException($"PayPal error {statusCode}: {message}{issueText} (debug id {debugId ?? "n/a"}).", statusCode, debugId, issues);
    }

    // ---------------------------------------------------------------------------------------------
    // JSON helpers
    // ---------------------------------------------------------------------------------------------

    private static void EnsureNoBuyerChallenge(JsonElement root, string action)
    {
        var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
        var needsAction = string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase);

        if (!needsAction && root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            needsAction = links.EnumerateArray().Any(l =>
                l.TryGetProperty("rel", out var rel) && rel.ValueKind == JsonValueKind.String &&
                (rel.GetString()!.Contains("payer-action", StringComparison.OrdinalIgnoreCase) ||
                 rel.GetString()!.Contains("3ds", StringComparison.OrdinalIgnoreCase)));
        }

        if (needsAction)
        {
            throw new PaymentException(
                PaymentErrorReason.ChallengeRequired,
                $"PayPal requires the shopper to approve in a browser (e.g. 3-D Secure) to {action}. This integration does not perform a browser approval round-trip.");
        }
    }

    private static string? DescribeCard(JsonElement root)
    {
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var card))
        {
            var brand = GetString(card, "brand");
            var last = GetString(card, "last_digits");
            if (!string.IsNullOrEmpty(brand) || !string.IsNullOrEmpty(last))
            {
                return $"{brand ?? "Card"}{(string.IsNullOrEmpty(last) ? string.Empty : $" ending {last}")}";
            }
        }
        return null;
    }

    private static JsonElement? FindFirst(JsonElement root, string arrayProp, string nestedObj, string nestedArray)
    {
        if (root.TryGetProperty(arrayProp, out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var unit in arr.EnumerateArray())
            {
                if (unit.TryGetProperty(nestedObj, out var payments) &&
                    payments.TryGetProperty(nestedArray, out var authorizations) &&
                    authorizations.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in authorizations.EnumerateArray())
                    {
                        return item;
                    }
                }
            }
        }
        return null;
    }

    private static decimal? MoneyValue(JsonElement parent, string prop)
        => parent.TryGetProperty(prop, out var money) && money.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String
            ? PayPalMoney.TryParse(v.GetString())
            : null;

    private static string? MoneyCurrency(JsonElement parent, string prop)
        => parent.TryGetProperty(prop, out var money) && money.TryGetProperty("currency_code", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString()
            : null;

    private static string? GetString(JsonElement parent, string prop)
        => parent.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? TryGetDebugId(JsonElement root)
        => root.TryGetProperty("debug_id", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    private static string FormatSearchDate(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

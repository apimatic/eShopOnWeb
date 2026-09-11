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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The single place that knows how to talk to PayPal's REST API. Handles OAuth token caching, idempotency
/// headers, transient-error retries, pagination, and translation of PayPal errors into domain exceptions.
/// Never logs card details or credentials.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private const string TokenCacheKey = "paypal:access_token";
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<PayPalClient> _logger;
    private readonly string _baseUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public PayPalClient(HttpClient http, PayPalSettings settings, IMemoryCache cache, IAppLogger<PayPalClient> logger)
    {
        _http = http;
        _settings = settings;
        _cache = cache;
        _logger = logger;
        _baseUrl = settings.ResolveBaseUrl();
    }

    private string Fmt(decimal amount) => Money.Format(amount, _settings.CurrencyDecimals);

    // --- Authorize -----------------------------------------------------------------------------

    public async Task<AuthorizationResult> AuthorizeAsync(decimal amount, string currency, string invoiceId, PaymentSource source, string requestId, CancellationToken cancellationToken = default)
    {
        var card = BuildCardNode(source);
        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["invoice_id"] = invoiceId,
                    ["custom_id"] = invoiceId,
                    ["amount"] = new { currency_code = currency, value = Fmt(amount) },
                },
            },
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = card },
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId, prefer: "return=representation", cancellationToken);
        var root = doc!.RootElement;

        var payPalOrderId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() : null;

        var authorization = FindFirstAuthorization(root);
        if (authorization is null)
        {
            // No authorization in the create response. Either a browser challenge is required, or the order
            // was merely created/approved and still needs an explicit authorize call.
            GuardAgainstChallenge(root, payPalOrderId);

            using var authDoc = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize",
                new Dictionary<string, object?>(), requestId + "-auth", prefer: "return=representation", cancellationToken);
            var authRoot = authDoc!.RootElement;
            GuardAgainstChallenge(authRoot, payPalOrderId);
            authorization = FindFirstAuthorization(authRoot);
            status = authRoot.TryGetProperty("status", out var st2) ? st2.GetString() : status;
        }

        if (authorization is null)
        {
            throw new PayPalApiException(
                $"PayPal did not return an authorization for order {payPalOrderId} (status: {status ?? "unknown"}).",
                (int)HttpStatusCode.BadGateway, null, status);
        }

        var auth = authorization.Value;
        var authId = auth.GetProperty("id").GetString()!;
        var authStatus = auth.TryGetProperty("status", out var asv) ? asv.GetString() ?? "CREATED" : "CREATED";
        DateTimeOffset? expires = auth.TryGetProperty("expiration_time", out var exp) && exp.ValueKind == JsonValueKind.String
            ? ParseDate(exp.GetString())
            : null;

        return new AuthorizationResult(payPalOrderId, authId, authStatus, expires);
    }

    // --- Capture -------------------------------------------------------------------------------

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal? amount, string currency, string requestId, bool finalCapture, CancellationToken cancellationToken = default)
    {
        // invoice_id/custom_id are inherited from the authorizing order, so we do not re-send them here
        // (the merchant account enforces unique invoice ids per transaction).
        var body = new Dictionary<string, object?>
        {
            ["final_capture"] = finalCapture,
        };
        if (amount.HasValue)
        {
            body["amount"] = new { currency_code = currency, value = Fmt(amount.Value) };
        }

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, requestId, prefer: "return=representation", cancellationToken);
        var root = doc!.RootElement;

        var captureId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "COMPLETED" : "COMPLETED";

        decimal gross = amount ?? 0m, fee = 0m, net = amount ?? 0m;
        string currencyCode = currency;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            gross = ReadMoney(breakdown, "gross_amount", out currencyCode) ?? gross;
            fee = ReadMoney(breakdown, "paypal_fee", out _) ?? 0m;
            net = ReadMoney(breakdown, "net_amount", out _) ?? (gross - fee);
        }
        else if (root.TryGetProperty("amount", out var amt))
        {
            gross = ReadMoneyValue(amt, out currencyCode) ?? gross;
            net = gross;
        }

        return new CaptureResult(captureId, status, gross, fee, net, currencyCode);
    }

    // --- Reauthorize ---------------------------------------------------------------------------

    public async Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = new { currency_code = currency, value = Fmt(amount) },
        };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, requestId, prefer: "return=representation", cancellationToken);
        var root = doc!.RootElement;

        var id = root.TryGetProperty("id", out var idv) ? idv.GetString() ?? authorizationId : authorizationId;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "CREATED" : "CREATED";
        DateTimeOffset? expires = root.TryGetProperty("expiration_time", out var exp) && exp.ValueKind == JsonValueKind.String
            ? ParseDate(exp.GetString())
            : null;

        return new ReauthorizeResult(id, status, expires);
    }

    // --- Void ----------------------------------------------------------------------------------

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", body: null, requestId, prefer: null, cancellationToken);
    }

    // --- Refund --------------------------------------------------------------------------------

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        // invoice_id/custom_id are inherited from the capture; not re-sent (unique-invoice account setting).
        var body = new Dictionary<string, object?>();
        if (amount.HasValue)
        {
            body["amount"] = new { currency_code = currency, value = Fmt(amount.Value) };
        }

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, requestId, prefer: "return=representation", cancellationToken);
        var root = doc!.RootElement;

        var refundId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "COMPLETED" : "COMPLETED";

        decimal refundedAmount = amount ?? 0m;
        string currencyCode = currency;
        if (root.TryGetProperty("amount", out var amt))
        {
            refundedAmount = ReadMoneyValue(amt, out currencyCode) ?? refundedAmount;
        }

        decimal totalRefunded = refundedAmount;
        if (root.TryGetProperty("seller_payable_breakdown", out var breakdown))
        {
            totalRefunded = ReadMoney(breakdown, "total_refunded_amount", out _) ?? refundedAmount;
        }

        return new RefundResult(refundId, status, refundedAmount, totalRefunded, currencyCode);
    }

    // --- Vault ---------------------------------------------------------------------------------

    public async Task<VaultCardResult> VaultCardAsync(CardDetails card, string? customerId, string requestId, CancellationToken cancellationToken = default)
    {
        var cardNode = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = NormalizeExpiry(card.Expiry),
            ["security_code"] = card.SecurityCode,
            ["name"] = card.CardholderName,
            ["billing_address"] = BuildBillingAddress(card.BillingAddress),
        };

        var body = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = cardNode },
        };
        if (!string.IsNullOrEmpty(customerId))
        {
            body["customer"] = new { id = customerId };
        }

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, requestId, prefer: "return=representation", cancellationToken);
        var root = doc!.RootElement;

        var tokenId = root.GetProperty("id").GetString()!;
        string? returnedCustomerId = root.TryGetProperty("customer", out var cust) && cust.TryGetProperty("id", out var cid)
            ? cid.GetString()
            : customerId;

        string brand = "UNKNOWN", last4 = "****";
        string? expiry = null, name = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardResp))
        {
            brand = cardResp.TryGetProperty("brand", out var b) ? b.GetString() ?? brand : brand;
            last4 = cardResp.TryGetProperty("last_digits", out var l) ? l.GetString() ?? last4 : last4;
            expiry = cardResp.TryGetProperty("expiry", out var e) ? e.GetString() : null;
            name = cardResp.TryGetProperty("name", out var n) ? n.GetString() : null;
        }

        return new VaultCardResult(tokenId, returnedCustomerId, brand, last4, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{paymentTokenId}", body: null, requestId: null, prefer: null, cancellationToken);
    }

    // --- Transaction search --------------------------------------------------------------------

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();

        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxSearchWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            var page = 1;
            int totalPages;
            do
            {
                var query = "/v1/reporting/transactions" +
                            $"?start_date={Uri.EscapeDataString(FormatDate(windowStart))}" +
                            $"&end_date={Uri.EscapeDataString(FormatDate(windowEnd))}" +
                            "&fields=all&balance_affecting_records_only=N&page_size=100" +
                            $"&page={page}";

                using var doc = await SendAsync(HttpMethod.Get, query, body: null, requestId: null, prefer: null, cancellationToken);
                var root = doc!.RootElement;

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (detail.TryGetProperty("transaction_info", out var info))
                        {
                            results.Add(MapTransaction(info));
                        }
                    }
                }

                totalPages = root.TryGetProperty("total_pages", out var tp) && tp.ValueKind == JsonValueKind.Number ? tp.GetInt32() : 1;
                page++;
            }
            while (page <= totalPages);

            // Advance a second past the window end to avoid re-fetching the boundary transaction.
            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    private static PayPalTransaction MapTransaction(JsonElement info)
    {
        string txnId = info.TryGetProperty("transaction_id", out var t) ? t.GetString() ?? string.Empty : string.Empty;
        string? eventCode = info.TryGetProperty("transaction_event_code", out var ec) ? ec.GetString() : null;
        string? status = info.TryGetProperty("transaction_status", out var ts) ? ts.GetString() : null;
        string? invoiceId = info.TryGetProperty("invoice_id", out var inv) ? inv.GetString() : null;
        string? customField = info.TryGetProperty("custom_field", out var cf) ? cf.GetString() : null;

        decimal amount = 0m, fee = 0m;
        string currency = string.Empty;
        if (info.TryGetProperty("transaction_amount", out var amt))
        {
            amount = ReadMoneyValue(amt, out currency) ?? 0m;
        }
        if (info.TryGetProperty("fee_amount", out var feeAmt))
        {
            fee = ReadMoneyValue(feeAmt, out _) ?? 0m;
        }

        DateTimeOffset? date = info.TryGetProperty("transaction_initiation_date", out var d) && d.ValueKind == JsonValueKind.String
            ? ParseDate(d.GetString())
            : null;

        return new PayPalTransaction(txnId, eventCode, status, amount, fee, currency, date, invoiceId, customField);
    }

    // --- HTTP plumbing -------------------------------------------------------------------------

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string pathAndQuery, object? body, string? requestId, string? prefer, CancellationToken cancellationToken)
    {
        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            var token = await GetAccessTokenAsync(forceRefresh: false, cancellationToken);
            using var request = BuildRequest(method, pathAndQuery, body, requestId, prefer, token);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                await BackoffAsync(attempt, cancellationToken);
                _logger.LogWarning($"PayPal {method} {Redact(pathAndQuery)} transport error (attempt {attempt}): {ex.Message}");
                continue;
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
                {
                    // Token may have expired server-side; refresh once and retry.
                    await GetAccessTokenAsync(forceRefresh: true, cancellationToken);
                    continue;
                }

                if (((int)response.StatusCode == 429 || (int)response.StatusCode >= 500) && attempt < maxAttempts)
                {
                    await BackoffAsync(attempt, cancellationToken);
                    _logger.LogWarning($"PayPal {method} {Redact(pathAndQuery)} returned {(int)response.StatusCode} (attempt {attempt}); retrying.");
                    continue;
                }

                var payload = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    throw BuildApiException(response, payload);
                }

                if (string.IsNullOrWhiteSpace(payload))
                {
                    return null; // e.g. 204 No Content
                }
                return JsonDocument.Parse(payload);
            }
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string pathAndQuery, object? body, string? requestId, string? prefer, string token)
    {
        var request = new HttpRequestMessage(method, _baseUrl + pathAndQuery);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (!string.IsNullOrEmpty(prefer))
        {
            request.Headers.TryAddWithoutValidation("Prefer", prefer);
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private PayPalApiException BuildApiException(HttpResponseMessage response, string payload)
    {
        string? debugId = null, name = null;
        var issues = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("debug_id", out var d)) debugId = d.GetString();
            if (root.TryGetProperty("name", out var n)) name = n.GetString();
            if (root.TryGetProperty("message", out var m) && string.IsNullOrEmpty(name)) name = m.GetString();
            if (root.TryGetProperty("details", out var det) && det.ValueKind == JsonValueKind.Array)
            {
                foreach (var issue in det.EnumerateArray())
                {
                    var issueName = issue.TryGetProperty("issue", out var iv) ? iv.GetString() : null;
                    var desc = issue.TryGetProperty("description", out var dv) ? dv.GetString() : null;
                    issues.Add(string.Join(": ", new[] { issueName, desc }.Where(s => !string.IsNullOrEmpty(s))));
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with what we have.
        }

        var issueText = issues.Count > 0 ? $" Issues: {string.Join("; ", issues)}." : string.Empty;
        var message = $"PayPal API error {(int)response.StatusCode} ({name ?? "unknown"}).{issueText} debug_id: {debugId ?? "n/a"}.";
        _logger.LogWarning(message);
        return new PayPalApiException(message, (int)response.StatusCode, debugId, name ?? string.Join(",", issues));
    }

    private static async Task BackoffAsync(int attempt, CancellationToken cancellationToken)
    {
        var delayMs = (int)(Math.Pow(2, attempt) * 200) + Random.Shared.Next(0, 250);
        await Task.Delay(delayMs, cancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _cache.TryGetValue<string>(TokenCacheKey, out var cached) && !string.IsNullOrEmpty(cached))
        {
            return cached!;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/oauth2/token");
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });

        using var response = await _http.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildApiException(response, payload);
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var token = root.GetProperty("access_token").GetString()!;
        var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3000;

        _cache.Set(TokenCacheKey, token, TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60)));
        return token;
    }

    // --- JSON helpers --------------------------------------------------------------------------

    private object? BuildCardNode(PaymentSource source)
    {
        if (!string.IsNullOrEmpty(source.VaultId))
        {
            return new Dictionary<string, object?> { ["vault_id"] = source.VaultId };
        }

        var card = source.Card ?? throw new PaymentValidationException("A card or a saved card id is required to authorize a payment.");
        return new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = NormalizeExpiry(card.Expiry),
            ["security_code"] = card.SecurityCode,
            ["name"] = card.CardholderName,
            ["billing_address"] = BuildBillingAddress(card.BillingAddress),
        };
    }

    private static object? BuildBillingAddress(PayPalAddress? address)
    {
        if (address is null)
        {
            return null;
        }
        return new Dictionary<string, object?>
        {
            ["address_line_1"] = address.AddressLine1,
            ["address_line_2"] = address.AddressLine2,
            ["admin_area_2"] = address.AdminArea2,
            ["admin_area_1"] = address.AdminArea1,
            ["postal_code"] = address.PostalCode,
            ["country_code"] = string.IsNullOrWhiteSpace(address.CountryCode) ? "US" : address.CountryCode,
        };
    }

    private static JsonElement? FindFirstAuthorization(JsonElement root)
    {
        if (!root.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments)
                && payments.TryGetProperty("authorizations", out var auths)
                && auths.ValueKind == JsonValueKind.Array)
            {
                foreach (var auth in auths.EnumerateArray())
                {
                    return auth.Clone();
                }
            }
        }
        return null;
    }

    private static void GuardAgainstChallenge(JsonElement root, string orderId)
    {
        var status = root.TryGetProperty("status", out var st) ? st.GetString() : null;
        var requiresAction = string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase);

        if (!requiresAction && root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            requiresAction = links.EnumerateArray().Any(l =>
                l.TryGetProperty("rel", out var rel) &&
                (string.Equals(rel.GetString(), "payer-action", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(rel.GetString(), "approve", StringComparison.OrdinalIgnoreCase)));
        }

        if (requiresAction)
        {
            throw new PayPalChallengeRequiredException(
                $"PayPal requires the shopper to approve payment for order {orderId} in a browser (status: {status}). " +
                "This integration does not perform browser approval round-trips.");
        }
    }

    private static decimal? ReadMoney(JsonElement parent, string property, out string currency)
    {
        currency = string.Empty;
        if (parent.TryGetProperty(property, out var money))
        {
            return ReadMoneyValue(money, out currency);
        }
        return null;
    }

    private static decimal? ReadMoneyValue(JsonElement money, out string currency)
    {
        currency = money.TryGetProperty("currency_code", out var c) ? c.GetString() ?? string.Empty : string.Empty;
        if (money.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
        {
            return Money.Parse(v.GetString());
        }
        return null;
    }

    /// <summary>Normalizes a card expiry to PayPal's YYYY-MM format, accepting MM/YY, MM/YYYY, or YYYY-MM.</summary>
    internal static string NormalizeExpiry(string expiry)
    {
        var trimmed = (expiry ?? string.Empty).Trim();
        if (trimmed.Length == 7 && trimmed[4] == '-')
        {
            return trimmed; // already YYYY-MM
        }

        var parts = trimmed.Split('/', '-');
        if (parts.Length == 2)
        {
            var month = parts[0].PadLeft(2, '0');
            var year = parts[1];
            if (year.Length == 2)
            {
                year = "20" + year;
            }
            if (year.Length == 4 && month.Length == 2)
            {
                return $"{year}-{month}";
            }
            // Handle YYYY-MM given as parts[0]=year.
            if (parts[0].Length == 4)
            {
                return $"{parts[0]}-{parts[1].PadLeft(2, '0')}";
            }
        }
        return trimmed;
    }

    private static string FormatDate(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    private static string Redact(string pathAndQuery)
    {
        var idx = pathAndQuery.IndexOf('?');
        return idx >= 0 ? pathAndQuery[..idx] : pathAndQuery;
    }
}

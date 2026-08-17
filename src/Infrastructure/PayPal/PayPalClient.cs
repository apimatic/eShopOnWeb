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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Typed HTTP gateway over the PayPal REST API (Orders v2, Payments v2, Vault v3, Reporting v1).
/// Handles OAuth token caching, idempotency headers, and transient-failure retries. This is the
/// only component that speaks PayPal's wire protocol; the rest of the app depends on
/// <see cref="IPayPalClient"/>. Full card details flow through here transiently and are never
/// persisted or logged.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private const string TokenCacheKey = "paypal:oauth_token";
    private static readonly TimeSpan MaxReportingWindow = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<PayPalClient> _logger;

    public PayPalClient(
        HttpClient http,
        PayPalSettings settings,
        IMemoryCache cache,
        IAppLogger<PayPalClient> logger)
    {
        _http = http;
        _settings = settings;
        _cache = cache;
        _logger = logger;
        _settings.Validate();
    }

    // ---------------------------------------------------------------- Authorize (raw card)

    public async Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(
        decimal amount, string currencyCode, PayPalCardDetails card, string invoiceReference, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[] { PurchaseUnit(amount, currencyCode, invoiceReference) },
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = CardObject(card) }
        };
        using var doc = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId, cancellationToken);
        return ParseAuthorization(doc.RootElement);
    }

    // ---------------------------------------------------------------- Authorize (vaulted card)

    public async Task<PayPalAuthorizationResult> AuthorizeWithVaultAsync(
        decimal amount, string currencyCode, string vaultId, string invoiceReference, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[] { PurchaseUnit(amount, currencyCode, invoiceReference) },
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = new Dictionary<string, object?> { ["vault_id"] = vaultId }
            }
        };
        using var doc = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId, cancellationToken);
        return ParseAuthorization(doc.RootElement);
    }

    // ---------------------------------------------------------------- Get authorization

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        var root = doc.RootElement;
        var (amount, currency) = ReadMoney(root, "amount");
        return new PayPalAuthorizationDetails(
            GetString(root, "id") ?? authorizationId,
            GetString(root, "status") ?? "UNKNOWN",
            ReadDate(root, "expiration_time"),
            amount, currency);
    }

    // ---------------------------------------------------------------- Capture

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, decimal amount, string currencyCode, string invoiceReference, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = Money(amount, currencyCode),
            ["invoice_id"] = invoiceReference,
            ["final_capture"] = true
        };
        using var doc = await SendJsonAsync(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, requestId, cancellationToken,
            preferRepresentation: true);
        var root = doc.RootElement;

        var (gross, currency) = ReadMoney(root, "amount");
        decimal fee = 0m, net = gross;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            (gross, currency) = ReadMoney(breakdown, "gross_amount", fallback: (gross, currency));
            fee = ReadMoney(breakdown, "paypal_fee", fallback: (0m, currency)).Amount;
            net = ReadMoney(breakdown, "net_amount", fallback: (gross - fee, currency)).Amount;
        }

        return new PayPalCaptureResult(
            GetString(root, "id")!, GetString(root, "status") ?? "COMPLETED", gross, fee, net, currency);
    }

    // ---------------------------------------------------------------- Reauthorize

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId, decimal amount, string currencyCode, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["amount"] = Money(amount, currencyCode) };
        using var doc = await SendJsonAsync(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, requestId, cancellationToken,
            preferRepresentation: true);
        var root = doc.RootElement;
        var (amt, currency) = ReadMoney(root, "amount", fallback: (amount, currencyCode));
        return new PayPalAuthorizationDetails(
            GetString(root, "id")!, GetString(root, "status") ?? "CREATED", ReadDate(root, "expiration_time"), amt, currency);
    }

    // ---------------------------------------------------------------- Void

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendJsonAsync(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, null, cancellationToken);
        // 204 No Content on success; SendJsonAsync tolerates an empty body.
    }

    // ---------------------------------------------------------------- Refund

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currencyCode, string invoiceReference, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["invoice_id"] = invoiceReference };
        if (amount is not null)
        {
            body["amount"] = Money(amount.Value, currencyCode);
        }
        // The caller's idempotency key, qualified by the capture id, becomes the PayPal-Request-Id so
        // PayPal itself never refunds twice — and so the same key against a different (later-run)
        // capture is not mistaken for a replay of an earlier one.
        var refundRequestId = $"refund-{captureId}-{idempotencyKey}";
        using var doc = await SendJsonAsync(
            HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, refundRequestId, cancellationToken,
            preferRepresentation: true);
        var root = doc.RootElement;
        var (amt, currency) = ReadMoney(root, "amount", fallback: (amount ?? 0m, currencyCode));
        return new PayPalRefundResult(GetString(root, "id")!, GetString(root, "status") ?? "COMPLETED", amt, currency);
    }

    // ---------------------------------------------------------------- Vault (save card)

    public async Task<PayPalVaultedCard> VaultCardAsync(PayPalCardDetails card, string requestId, CancellationToken cancellationToken = default)
    {
        // 1) Create a setup token from the raw card.
        var setupBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = CardObject(card) }
        };
        string setupTokenId;
        using (var setupDoc = await SendJsonAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody, requestId, cancellationToken))
        {
            setupTokenId = GetString(setupDoc.RootElement, "id")
                ?? throw new PayPalApiException("PayPal did not return a setup token id.", 502, null, null);
        }

        // 2) Exchange the setup token for a permanent payment (vault) token.
        var confirmBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?> { ["id"] = setupTokenId, ["type"] = "SETUP_TOKEN" }
            }
        };
        using var doc = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens", confirmBody, $"{requestId}-confirm", cancellationToken);
        var root = doc.RootElement;

        var vaultId = GetString(root, "id")
            ?? throw new PayPalApiException("PayPal did not return a vault token id.", 502, null, null);
        string? customerId = root.TryGetProperty("customer", out var cust) ? GetString(cust, "id") : null;

        string brand = "Card", last = "0000", expiry = string.Empty;
        string? name = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            brand = GetString(cardEl, "brand") ?? brand;
            last = GetString(cardEl, "last_digits") ?? last;
            expiry = GetString(cardEl, "expiry") ?? expiry;
            name = GetString(cardEl, "name");
        }

        return new PayPalVaultedCard(vaultId, customerId, brand, last, expiry, name);
    }

    // ---------------------------------------------------------------- Delete vaulted card

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendJsonAsync(
            HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, cancellationToken);
        // 204 No Content on success.
    }

    // ---------------------------------------------------------------- Transaction search (reconciliation)

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // De-duplicate across window boundaries by transaction id.
        var byId = new Dictionary<string, PayPalTransaction>(StringComparer.OrdinalIgnoreCase);

        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxReportingWindow;
            if (windowEnd > to) windowEnd = to;

            await CollectWindowAsync(windowStart, windowEnd, byId, cancellationToken);

            if (windowEnd >= to) break;
            windowStart = windowEnd;
        }

        return byId.Values.OrderBy(t => t.Date).ToList();
    }

    private async Task CollectWindowAsync(
        DateTimeOffset start, DateTimeOffset end, IDictionary<string, PayPalTransaction> sink, CancellationToken cancellationToken)
    {
        int page = 1;
        int totalPages = 1;
        do
        {
            var query =
                $"?start_date={Uri.EscapeDataString(FormatInstant(start))}" +
                $"&end_date={Uri.EscapeDataString(FormatInstant(end))}" +
                $"&fields=transaction_info&page_size=500&page={page}";

            using var doc = await SendJsonAsync(
                HttpMethod.Get, $"/v1/reporting/transactions{query}", null, null, cancellationToken);
            var root = doc.RootElement;

            if (root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var parsedTotal))
            {
                totalPages = parsedTotal;
            }

            if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in details.EnumerateArray())
                {
                    if (!d.TryGetProperty("transaction_info", out var info)) continue;
                    var txn = ParseTransaction(info);
                    if (txn is not null) sink[txn.TransactionId] = txn;
                }
            }

            page++;
        }
        while (page <= totalPages);
    }

    private static PayPalTransaction? ParseTransaction(JsonElement info)
    {
        var id = GetString(info, "transaction_id");
        if (string.IsNullOrWhiteSpace(id)) return null;

        var (amount, currency) = ReadMoney(info, "transaction_amount");
        var fee = ReadMoney(info, "fee_amount", fallback: (0m, currency)).Amount;

        return new PayPalTransaction(
            id!,
            GetString(info, "paypal_reference_id"),
            GetString(info, "transaction_event_code") ?? string.Empty,
            GetString(info, "transaction_status") ?? string.Empty,
            amount, fee, currency,
            ReadDate(info, "transaction_initiation_date") ?? DateTimeOffset.MinValue,
            GetString(info, "invoice_id"));
    }

    // ---------------------------------------------------------------- HTTP plumbing

    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool preferRepresentation = false)
    {
        const int maxAttempts = 4;
        for (int attempt = 1; ; attempt++)
        {
            var token = await GetAccessTokenAsync(cancellationToken);

            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (!string.IsNullOrEmpty(requestId))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }
            if (preferRepresentation)
            {
                request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            }
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                await BackoffAsync(attempt, null, cancellationToken);
                _logger.LogWarning("PayPal {0} {1} network error (attempt {2}): {3}", method, path, attempt, ex.Message);
                continue;
            }

            using (response)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return string.IsNullOrWhiteSpace(content)
                        ? JsonDocument.Parse("{}")
                        : JsonDocument.Parse(content);
                }

                // Retry on rate limiting and transient server errors.
                bool transient = response.StatusCode == HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode >= 500;
                if (transient && attempt < maxAttempts)
                {
                    await BackoffAsync(attempt, response.Headers.RetryAfter?.Delta, cancellationToken);
                    _logger.LogWarning("PayPal {0} {1} returned {2} (attempt {3}); retrying.",
                        method, path, (int)response.StatusCode, attempt);
                    continue;
                }

                throw BuildApiException(method, path, response.StatusCode, content);
            }
        }
    }

    private PayPalApiException BuildApiException(HttpMethod method, string path, HttpStatusCode status, string content)
    {
        string? debugId = null, issue = null, name = null, message = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
            var root = doc.RootElement;
            name = GetString(root, "name");
            message = GetString(root, "message");
            debugId = GetString(root, "debug_id");
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
                && details.GetArrayLength() > 0)
            {
                issue = GetString(details[0], "issue");
            }
        }
        catch (JsonException) { /* non-JSON error body */ }

        var summary = $"PayPal {method} {path} failed with {(int)status} {status}" +
            (name is not null ? $" ({name})" : "") +
            (issue is not null ? $" [{issue}]" : "") +
            (message is not null ? $": {message}" : "") +
            (debugId is not null ? $" (debug_id={debugId})" : "");
        _logger.LogWarning("{0}", summary);
        return new PayPalApiException(summary, (int)status, debugId, issue ?? name);
    }

    private static async Task BackoffAsync(int attempt, TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        var delay = retryAfter ?? TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
        await Task.Delay(delay, cancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<string>(TokenCacheKey, out var cached) && !string.IsNullOrEmpty(cached))
        {
            return cached!;
        }

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        using var response = await _http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildApiException(HttpMethod.Post, "/v1/oauth2/token", response.StatusCode, content);
        }

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        var token = GetString(root, "access_token")
            ?? throw new PayPalApiException("PayPal token response had no access_token.", 502, null, null);
        var expiresIn = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var secs) ? secs : 3000;

        // Refresh a minute early to avoid using a just-expired token.
        var ttl = TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60));
        _cache.Set(TokenCacheKey, token, ttl);
        return token;
    }

    // ---------------------------------------------------------------- payload / parse helpers

    private static Dictionary<string, object?> PurchaseUnit(decimal amount, string currencyCode, string invoiceReference) => new()
    {
        ["reference_id"] = "default",
        ["invoice_id"] = invoiceReference,
        ["custom_id"] = invoiceReference,
        ["amount"] = Money(amount, currencyCode)
    };

    private static Dictionary<string, object?> Money(decimal amount, string currencyCode) => new()
    {
        ["currency_code"] = currencyCode,
        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static Dictionary<string, object?> CardObject(PayPalCardDetails card)
    {
        var obj = new Dictionary<string, object?>
        {
            ["number"] = card.Number?.Replace(" ", string.Empty),
            ["expiry"] = NormalizeExpiry(card.Expiry),
            ["security_code"] = card.SecurityCode
        };
        if (!string.IsNullOrWhiteSpace(card.Name)) obj["name"] = card.Name;

        if (!string.IsNullOrWhiteSpace(card.BillingAddressLine1))
        {
            var address = new Dictionary<string, object?>
            {
                ["address_line_1"] = card.BillingAddressLine1
            };
            if (!string.IsNullOrWhiteSpace(card.BillingAddressLine2)) address["address_line_2"] = card.BillingAddressLine2;
            if (!string.IsNullOrWhiteSpace(card.BillingCity)) address["admin_area_2"] = card.BillingCity;
            if (!string.IsNullOrWhiteSpace(card.BillingState)) address["admin_area_1"] = card.BillingState;
            if (!string.IsNullOrWhiteSpace(card.BillingPostalCode)) address["postal_code"] = card.BillingPostalCode;
            if (!string.IsNullOrWhiteSpace(card.BillingCountryCode)) address["country_code"] = card.BillingCountryCode;
            obj["billing_address"] = address;
        }
        return obj;
    }

    /// <summary>Normalizes a card expiry to PayPal's "YYYY-MM". Accepts "YYYY-MM", "MM/YY", or "MM/YYYY".</summary>
    internal static string NormalizeExpiry(string expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry)) return expiry;
        expiry = expiry.Trim();

        if (expiry.Length == 7 && expiry[4] == '-') return expiry; // already YYYY-MM

        if (expiry.Contains('/'))
        {
            var parts = expiry.Split('/');
            if (parts.Length == 2)
            {
                var mm = parts[0].PadLeft(2, '0');
                var yy = parts[1];
                var yyyy = yy.Length == 2 ? $"20{yy}" : yy;
                return $"{yyyy}-{mm}";
            }
        }
        return expiry;
    }

    private PayPalAuthorizationResult ParseAuthorization(JsonElement order)
    {
        var orderId = GetString(order, "id") ?? throw new PayPalApiException("PayPal order had no id.", 502, null, null);
        var orderStatus = GetString(order, "status") ?? "UNKNOWN";

        // A challenge (3-D Secure / buyer approval) is surfaced, not worked around.
        if (string.Equals(orderStatus, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || HasLink(order, "payer-action") || HasLink(order, "approve"))
        {
            throw new PayPalChallengeRequiredException(
                $"PayPal requires buyer approval in a browser for order {orderId} (status {orderStatus}). " +
                "This integration only supports direct card payments that complete without a browser step.");
        }

        JsonElement authorization = default;
        bool found = false;
        if (order.TryGetProperty("purchase_units", out var units) && units.ValueKind == JsonValueKind.Array)
        {
            foreach (var unit in units.EnumerateArray())
            {
                if (unit.TryGetProperty("payments", out var payments)
                    && payments.TryGetProperty("authorizations", out var auths)
                    && auths.ValueKind == JsonValueKind.Array && auths.GetArrayLength() > 0)
                {
                    authorization = auths[0];
                    found = true;
                    break;
                }
            }
        }

        if (!found)
        {
            throw new PayPalApiException(
                $"PayPal order {orderId} (status {orderStatus}) returned no authorization to act on.", 502, null, null);
        }

        string? brand = null, last = null, cardExpiry = null;
        if (order.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            brand = GetString(cardEl, "brand");
            last = GetString(cardEl, "last_digits");
            cardExpiry = GetString(cardEl, "expiry");
        }

        return new PayPalAuthorizationResult(
            orderId, orderStatus,
            GetString(authorization, "id")!,
            GetString(authorization, "status") ?? "CREATED",
            ReadDate(authorization, "expiration_time"),
            brand, last, cardExpiry);
    }

    private static bool HasLink(JsonElement element, string rel)
    {
        if (!element.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array) return false;
        foreach (var link in links.EnumerateArray())
        {
            if (string.Equals(GetString(link, "rel"), rel, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string? GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static (decimal Amount, string CurrencyCode) ReadMoney(
        JsonElement parent, string property, (decimal Amount, string CurrencyCode)? fallback = null)
    {
        var fb = fallback ?? (0m, "USD");
        if (!parent.TryGetProperty(property, out var money) || money.ValueKind != JsonValueKind.Object)
        {
            return fb;
        }
        var value = GetString(money, "value");
        var currency = GetString(money, "currency_code") ?? fb.CurrencyCode;
        if (value is null || !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return (fb.Amount, currency);
        }
        return (amount, currency);
    }

    private static DateTimeOffset? ReadDate(JsonElement parent, string property)
    {
        var raw = GetString(parent, property);
        if (raw is null) return null;
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : null;
    }

    private static string FormatInstant(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}

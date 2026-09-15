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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Talks HTTP to the PayPal REST API (Orders v2, Payments v2, Vault v3, Transaction Search v1).
/// Every endpoint, field and response path used here was confirmed against the live PayPal sandbox
/// and official documentation. No raw card data is ever logged.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private const string TokenCacheKey = "paypal_access_token";

    // The reporting API allows a maximum 31-day window per request.
    private static readonly TimeSpan MaxReportingWindow = TimeSpan.FromDays(31);
    private const int ReportingPageSize = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(
        HttpClient httpClient,
        IOptions<PayPalSettings> settings,
        IMemoryCache cache,
        IAppLogger<PayPalPaymentGateway> logger)
    {
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(ResolveBaseUrl(_settings));
    }

    private static string ResolveBaseUrl(PayPalSettings settings)
    {
        // An explicit BaseUrl override wins for every call (including the token request).
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return settings.BaseUrl!.TrimEnd('/') + "/";
        }

        var env = (settings.Environment ?? "sandbox").Trim().ToLowerInvariant();
        return env is "live" or "production"
            ? "https://api-m.paypal.com/"
            : "https://api-m.sandbox.paypal.com/";
    }

    // ---------------------------------------------------------------------
    // Authentication
    // ---------------------------------------------------------------------

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached!;
        }

        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new PaymentGatewayException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret (e.g. via user-secrets).");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
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
            throw new PaymentGatewayException($"PayPal authentication failed ({(int)response.StatusCode}): {Summarize(body)}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var token = root.GetProperty("access_token").GetString()!;
        var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3000;

        // Cache with a safety buffer so we never present an about-to-expire token.
        var lifetime = TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60));
        _cache.Set(TokenCacheKey, token, lifetime);
        return token;
    }

    // ---------------------------------------------------------------------
    // Generic request helper
    // ---------------------------------------------------------------------

    private async Task<JsonDocument?> SendJsonAsync(
        HttpMethod method,
        string relativeUrl,
        object? body,
        string? idempotencyKey,
        bool preferRepresentation,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
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

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PaymentGatewayException(DescribeError(response.StatusCode, responseBody));
        }

        if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        return JsonDocument.Parse(responseBody);
    }

    // ---------------------------------------------------------------------
    // Authorizations (the hold)
    // ---------------------------------------------------------------------

    public Task<CardAuthorizationResult> AuthorizeWithCardAsync(Money amount, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
        => CreateAuthorizationAsync(amount, BuildRawCardSource(card), idempotencyKey, cancellationToken);

    public Task<CardAuthorizationResult> AuthorizeWithVaultedCardAsync(Money amount, string vaultId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var source = new Dictionary<string, object?>
        {
            ["card"] = new Dictionary<string, object?> { ["vault_id"] = vaultId }
        };
        return CreateAuthorizationAsync(amount, source, idempotencyKey, cancellationToken);
    }

    private async Task<CardAuthorizationResult> CreateAuthorizationAsync(
        Money amount, Dictionary<string, object?> paymentSource, string idempotencyKey, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new Dictionary<string, object?> { ["amount"] = BuildAmount(amount) }
            },
            ["payment_source"] = paymentSource
        };

        using var doc = await SendJsonAsync(HttpMethod.Post, "v2/checkout/orders", body, idempotencyKey, preferRepresentation: true, cancellationToken)
            ?? throw new PaymentGatewayException("PayPal returned an empty response when creating the authorization.");
        var root = doc.RootElement;

        var orderId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() : null;

        // A card that needs a browser challenge is out of scope for this browser-free integration.
        if (IsPayerActionRequired(status, root))
        {
            throw new PaymentChallengeRequiredException(
                $"PayPal requires the shopper to approve this card in a browser (order {orderId}, status {status}). " +
                "This integration is browser-free and cannot complete a payer-approval round-trip.");
        }

        var authorization = FindFirstAuthorization(root)
            ?? throw new PaymentGatewayException($"PayPal created order {orderId} with status {status} but returned no authorization to act on.");

        var authId = authorization.GetProperty("id").GetString()!;
        var authStatus = authorization.TryGetProperty("status", out var aSt) ? aSt.GetString() ?? "CREATED" : "CREATED";
        DateTimeOffset? expiresAt = TryGetDateTime(authorization, "expiration_time");

        var (brand, last4, cardExpiry) = ReadCardEcho(root);

        return new CardAuthorizationResult(orderId, authId, authStatus, expiresAt, brand, last4, cardExpiry);
    }

    // ---------------------------------------------------------------------
    // Capture (fulfilment)
    // ---------------------------------------------------------------------

    public async Task<CaptureResult> CaptureAsync(string authorizationId, Money amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = BuildAmount(amount),
            ["final_capture"] = true
        };

        using var doc = await SendJsonAsync(
            HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/capture", body, idempotencyKey, preferRepresentation: true, cancellationToken)
            ?? throw new PaymentGatewayException("PayPal returned an empty response when capturing the authorization.");
        var root = doc.RootElement;

        var captureId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "COMPLETED" : "COMPLETED";

        decimal gross;
        decimal? fee = null, net = null;
        string currency = amount.Currency;

        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            gross = ReadMoney(breakdown, "gross_amount", out var breakdownCurrency) ?? amount.Amount;
            currency = breakdownCurrency ?? amount.Currency;
            fee = ReadMoney(breakdown, "paypal_fee", out _);
            net = ReadMoney(breakdown, "net_amount", out _);
        }
        else
        {
            // Fall back to fetching the capture to obtain the fee/net breakdown.
            var fetched = await GetCaptureBreakdownAsync(captureId, cancellationToken);
            gross = fetched.gross ?? amount.Amount;
            fee = fetched.fee;
            net = fetched.net;
            currency = fetched.currency ?? amount.Currency;
        }

        return new CaptureResult(captureId, status, gross, fee, net, currency);
    }

    private async Task<(decimal? gross, decimal? fee, decimal? net, string? currency)> GetCaptureBreakdownAsync(string captureId, CancellationToken cancellationToken)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, $"v2/payments/captures/{captureId}", null, null, preferRepresentation: false, cancellationToken);
        if (doc is null || !doc.RootElement.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            return (null, null, null, null);
        }
        var gross = ReadMoney(breakdown, "gross_amount", out var currency);
        var fee = ReadMoney(breakdown, "paypal_fee", out _);
        var net = ReadMoney(breakdown, "net_amount", out _);
        return (gross, fee, net, currency);
    }

    // ---------------------------------------------------------------------
    // Void (cancel)
    // ---------------------------------------------------------------------

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void", null, null, preferRepresentation: false, cancellationToken);
    }

    // ---------------------------------------------------------------------
    // Reauthorize (renew a stale hold)
    // ---------------------------------------------------------------------

    public async Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, Money amount, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["amount"] = BuildAmount(amount) };

        using var doc = await SendJsonAsync(
            HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/reauthorize", body, null, preferRepresentation: true, cancellationToken)
            ?? throw new PaymentGatewayException("PayPal returned an empty response when reauthorizing.");
        var root = doc.RootElement;

        var newAuthId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "CREATED" : "CREATED";
        var expiresAt = TryGetDateTime(root, "expiration_time");
        return new ReauthorizationResult(newAuthId, status, expiresAt);
    }

    // ---------------------------------------------------------------------
    // Refund
    // ---------------------------------------------------------------------

    public async Task<RefundResult> RefundAsync(string captureId, Money? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object? body = amount is null
            ? null
            : new Dictionary<string, object?> { ["amount"] = BuildAmount(amount) };

        using var doc = await SendJsonAsync(
            HttpMethod.Post, $"v2/payments/captures/{captureId}/refund", body, idempotencyKey, preferRepresentation: true, cancellationToken)
            ?? throw new PaymentGatewayException("PayPal returned an empty response when refunding.");
        var root = doc.RootElement;

        var refundId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "COMPLETED" : "COMPLETED";
        var refundedAmount = amount?.Amount ?? ReadMoney(root, "amount", out _) ?? 0m;
        var currency = amount?.Currency ?? "USD";
        return new RefundResult(refundId, status, refundedAmount, currency);
    }

    // ---------------------------------------------------------------------
    // Vault (save / delete card)
    // ---------------------------------------------------------------------

    public async Task<VaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey, string? customerId = null, CancellationToken cancellationToken = default)
    {
        var cardNode = BuildCardNode(card);
        var paymentSource = new Dictionary<string, object?> { ["card"] = cardNode };
        var body = new Dictionary<string, object?> { ["payment_source"] = paymentSource };
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            body["customer"] = new Dictionary<string, object?> { ["id"] = customerId };
        }

        using var doc = await SendJsonAsync(HttpMethod.Post, "v3/vault/payment-tokens", body, idempotencyKey, preferRepresentation: false, cancellationToken)
            ?? throw new PaymentGatewayException("PayPal returned an empty response when vaulting the card.");
        var root = doc.RootElement;

        var vaultId = root.GetProperty("id").GetString()!;
        string? payPalCustomerId = null;
        if (root.TryGetProperty("customer", out var customer) && customer.TryGetProperty("id", out var cid))
        {
            payPalCustomerId = cid.GetString();
        }

        var (brand, last4, expiry, name) = ReadVaultedCardEcho(root);
        return new VaultedCard(vaultId, payPalCustomerId, brand, last4, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Delete, $"v3/vault/payment-tokens/{vaultId}", null, null, preferRepresentation: false, cancellationToken);
    }

    // ---------------------------------------------------------------------
    // Transaction search (reconciliation)
    // ---------------------------------------------------------------------

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var results = new List<PayPalTransaction>();

        // Walk the range in <=31-day windows so the whole range is covered, not just one window.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxReportingWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }
            if (windowEnd <= windowStart)
            {
                windowEnd = windowStart.AddSeconds(1);
            }

            await ReadTransactionWindowAsync(windowStart, windowEnd, results, cancellationToken);
            windowStart = windowEnd;
        }

        return results;
    }

    private async Task ReadTransactionWindowAsync(DateTimeOffset start, DateTimeOffset end, List<PayPalTransaction> sink, CancellationToken cancellationToken)
    {
        int page = 1;
        int totalPages = 1;

        do
        {
            var url = "v1/reporting/transactions"
                + $"?start_date={Uri.EscapeDataString(FormatReportingDate(start))}"
                + $"&end_date={Uri.EscapeDataString(FormatReportingDate(end))}"
                + "&fields=all"
                + $"&page_size={ReportingPageSize}"
                + $"&page={page}";

            using var doc = await SendJsonAsync(HttpMethod.Get, url, null, null, preferRepresentation: false, cancellationToken);
            if (doc is null)
            {
                return;
            }
            var root = doc.RootElement;

            if (root.TryGetProperty("total_pages", out var tp))
            {
                totalPages = tp.GetInt32();
            }

            if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (!detail.TryGetProperty("transaction_info", out var info))
                    {
                        continue;
                    }
                    var id = info.TryGetProperty("transaction_id", out var tid) ? tid.GetString() : null;
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }
                    var status = info.TryGetProperty("transaction_status", out var ts) ? ts.GetString() ?? "" : "";
                    var amount = ReadMoney(info, "transaction_amount", out var currency) ?? 0m;
                    var initiated = TryGetDateTime(info, "transaction_initiation_date");
                    var eventCode = info.TryGetProperty("transaction_event_code", out var ec) ? ec.GetString() : null;
                    sink.Add(new PayPalTransaction(id!, status, amount, currency ?? "", initiated, eventCode));
                }
            }

            page++;
        }
        while (page <= totalPages);
    }

    // ---------------------------------------------------------------------
    // JSON building / parsing helpers
    // ---------------------------------------------------------------------

    private Dictionary<string, object?> BuildRawCardSource(CardDetails card)
        => new() { ["card"] = BuildCardNode(card) };

    private static Dictionary<string, object?> BuildCardNode(CardDetails card)
    {
        var node = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = string.IsNullOrWhiteSpace(card.SecurityCode) ? null : card.SecurityCode,
            ["name"] = string.IsNullOrWhiteSpace(card.Name) ? null : card.Name
        };

        if (card.BillingAddress is not null)
        {
            var a = card.BillingAddress;
            var address = new Dictionary<string, object?>
            {
                ["address_line_1"] = a.AddressLine1,
                ["address_line_2"] = a.AddressLine2,
                ["admin_area_2"] = a.AdminArea2,
                ["admin_area_1"] = a.AdminArea1,
                ["postal_code"] = a.PostalCode,
                ["country_code"] = a.CountryCode
            };
            // Only attach if it carries something.
            if (address.Values.Any(v => v is not null))
            {
                node["billing_address"] = address;
            }
        }

        return node;
    }

    private Dictionary<string, object?> BuildAmount(Money money) => new()
    {
        ["currency_code"] = money.Currency,
        ["value"] = money.Amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static bool IsPayerActionRequired(string? status, JsonElement root)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
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

    private static JsonElement? FindFirstAuthorization(JsonElement root)
    {
        if (!root.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
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

    private static (string? brand, string? last4, string? expiry) ReadCardEcho(JsonElement root)
    {
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var card))
        {
            var brand = card.TryGetProperty("brand", out var b) ? b.GetString() : null;
            var last4 = card.TryGetProperty("last_digits", out var l) ? l.GetString() : null;
            var expiry = card.TryGetProperty("expiry", out var e) ? e.GetString() : null;
            return (brand, last4, expiry);
        }
        return (null, null, null);
    }

    private static (string? brand, string? last4, string? expiry, string? name) ReadVaultedCardEcho(JsonElement root)
    {
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var card))
        {
            var brand = card.TryGetProperty("brand", out var b) ? b.GetString() : null;
            var last4 = card.TryGetProperty("last_digits", out var l) ? l.GetString() : null;
            var expiry = card.TryGetProperty("expiry", out var e) ? e.GetString() : null;
            var name = card.TryGetProperty("name", out var n) ? n.GetString() : null;
            return (brand, last4, expiry, name);
        }
        return (null, null, null, null);
    }

    private static decimal? ReadMoney(JsonElement parent, string property, out string? currency)
    {
        currency = null;
        if (parent.TryGetProperty(property, out var money) && money.ValueKind == JsonValueKind.Object)
        {
            if (money.TryGetProperty("currency_code", out var cc))
            {
                currency = cc.GetString();
            }
            if (money.TryGetProperty("value", out var val))
            {
                var raw = val.GetString();
                if (!string.IsNullOrEmpty(raw) &&
                    decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }
        }
        return null;
    }

    private static DateTimeOffset? TryGetDateTime(JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String)
        {
            var raw = el.GetString();
            if (!string.IsNullOrEmpty(raw) &&
                DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            {
                return dto;
            }
        }
        return null;
    }

    private static string FormatReportingDate(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string DescribeError(HttpStatusCode statusCode, string body)
    {
        // Surface PayPal's own issue/description/debug id so operators get an actionable message,
        // without echoing anything sensitive (bodies never contain card numbers on the response path).
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            var debugId = root.TryGetProperty("debug_id", out var d) ? d.GetString() : null;

            string? issue = null, description = null;
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    issue = detail.TryGetProperty("issue", out var i) ? i.GetString() : issue;
                    description = detail.TryGetProperty("description", out var de) ? de.GetString() : description;
                    break;
                }
            }

            var sb = new StringBuilder($"PayPal request failed ({(int)statusCode})");
            if (!string.IsNullOrEmpty(name)) sb.Append($": {name}");
            if (!string.IsNullOrEmpty(issue)) sb.Append($" [{issue}]");
            if (!string.IsNullOrEmpty(description)) sb.Append($" - {description}");
            else if (!string.IsNullOrEmpty(message)) sb.Append($" - {message}");
            if (!string.IsNullOrEmpty(debugId)) sb.Append($" (debug_id {debugId})");
            return sb.ToString();
        }
        catch (JsonException)
        {
            return $"PayPal request failed ({(int)statusCode}): {Summarize(body)}";
        }
    }

    private static string Summarize(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "(no body)";
        body = body.Trim();
        return body.Length > 400 ? body.Substring(0, 400) + "..." : body;
    }
}

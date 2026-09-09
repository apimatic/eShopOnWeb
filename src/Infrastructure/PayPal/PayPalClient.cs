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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The single place that talks to PayPal's REST API. Everything about how to call PayPal
/// lives here; the rest of the app depends only on <see cref="IPayPalClient"/>.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private const string TokenCacheKey = "paypal:access_token";

    // Transaction Search allows at most a ~31-day window per request; chunk larger spans.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);
    private const int SearchPageSize = 100;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PayPalClient> _logger;

    public PayPalClient(HttpClient http, IOptions<PayPalSettings> options, IMemoryCache cache, ILogger<PayPalClient> logger)
    {
        _http = http;
        _settings = options.Value;
        _cache = cache;
        _logger = logger;
    }

    private string BaseUrl => _settings.ResolveBaseUrl();

    // -------------------- Orders / Authorize --------------------

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeRequest request, CancellationToken cancellationToken = default)
    {
        object cardNode = request.VaultId is not null
            ? new { vault_id = request.VaultId }
            : BuildCardNode(request.Card ?? throw new PaymentValidationException("A card or a saved paymentMethodId is required to pay."));

        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    invoice_id = request.InvoiceId,
                    custom_id = request.InvoiceId,
                    description = request.Description,
                    amount = new { currency_code = request.Currency, value = FormatAmount(request.Amount, request.Currency) }
                }
            },
            payment_source = new { card = cardNode }
        };

        using var doc = await SendJsonAsync(
            HttpMethod.Post,
            "/v2/checkout/orders",
            body,
            cancellationToken,
            headers: h =>
            {
                h.TryAddWithoutValidation("PayPal-Request-Id", request.RequestId);
                h.TryAddWithoutValidation("Prefer", "return=representation");
            });

        var root = doc!.RootElement;
        var orderId = root.GetProperty("id").GetString()!;
        var orderStatus = root.GetProperty("status").GetString() ?? "UNKNOWN";

        if (string.Equals(orderStatus, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentApprovalRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (e.g. 3-D Secure). " +
                "This integration does not perform a browser approval round-trip. Try a different card.");
        }

        var authorization = FindFirstPayment(root, "authorizations")
            ?? throw new PayPalApiException(
                $"PayPal order {orderId} was created with status {orderStatus} but returned no authorization.", 502);

        var authStatus = authorization.GetProperty("status").GetString() ?? "UNKNOWN";
        if (string.Equals(authStatus, "DENIED", StringComparison.OrdinalIgnoreCase))
            throw new PaymentDeclinedException($"PayPal denied the authorization for order {orderId}.");

        var (amount, currency) = ReadMoney(authorization.GetProperty("amount"));
        DateTimeOffset? expiresAt = authorization.TryGetProperty("expiration_time", out var exp) && exp.ValueKind == JsonValueKind.String
            ? DateTimeOffset.Parse(exp.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            : null;

        return new PayPalAuthorizationResult
        {
            PayPalOrderId = orderId,
            PayPalOrderStatus = orderStatus,
            AuthorizationId = authorization.GetProperty("id").GetString()!,
            AuthorizationStatus = authStatus,
            Amount = amount,
            Currency = currency,
            ExpiresAt = expiresAt
        };
    }

    // -------------------- Payments --------------------

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = new { currency_code = currency, value = FormatAmount(amount, currency) },
            final_capture = true
        };

        using var doc = await SendJsonAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            body,
            cancellationToken,
            headers: h =>
            {
                h.TryAddWithoutValidation("PayPal-Request-Id", requestId);
                h.TryAddWithoutValidation("Prefer", "return=representation");
            });

        var root = doc!.RootElement;
        var status = root.GetProperty("status").GetString() ?? "UNKNOWN";
        if (string.Equals(status, "DECLINED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentDeclinedException($"PayPal reported capture status {status} for authorization {authorizationId}.");
        }

        var (gross, curr) = ReadMoney(root.GetProperty("amount"));
        decimal fee = 0m, net = gross;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            if (breakdown.TryGetProperty("gross_amount", out var g)) gross = ReadMoney(g).amount;
            if (breakdown.TryGetProperty("paypal_fee", out var f)) fee = ReadMoney(f).amount;
            if (breakdown.TryGetProperty("net_amount", out var n)) net = ReadMoney(n).amount;
        }

        return new PayPalCaptureResult
        {
            CaptureId = root.GetProperty("id").GetString()!,
            Status = status,
            GrossAmount = gross,
            PayPalFee = fee,
            NetAmount = net,
            Currency = curr
        };
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new { amount = new { currency_code = currency, value = FormatAmount(amount, currency) } };

        using var doc = await SendJsonAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            body,
            cancellationToken,
            headers: h =>
            {
                h.TryAddWithoutValidation("PayPal-Request-Id", requestId);
                h.TryAddWithoutValidation("Prefer", "return=representation");
            });

        var root = doc!.RootElement;
        var (amt, curr) = ReadMoney(root.GetProperty("amount"));
        DateTimeOffset? expiresAt = root.TryGetProperty("expiration_time", out var exp) && exp.ValueKind == JsonValueKind.String
            ? DateTimeOffset.Parse(exp.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            : null;

        return new PayPalAuthorizationResult
        {
            PayPalOrderId = string.Empty,
            PayPalOrderStatus = "REAUTHORIZED",
            AuthorizationId = root.GetProperty("id").GetString()!,
            AuthorizationStatus = root.GetProperty("status").GetString() ?? "CREATED",
            Amount = amt,
            Currency = curr,
            ExpiresAt = expiresAt
        };
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendJsonAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            body: null,
            cancellationToken,
            headers: h => h.TryAddWithoutValidation("PayPal-Request-Id", requestId));
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object? body = amount.HasValue
            ? new { amount = new { currency_code = currency, value = FormatAmount(amount.Value, currency) } }
            : null;

        using var doc = await SendJsonAsync(
            HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            body,
            cancellationToken,
            headers: h =>
            {
                h.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
                h.TryAddWithoutValidation("Prefer", "return=representation");
            });

        var root = doc!.RootElement;
        decimal refundAmount = amount ?? 0m;
        string curr = currency;
        if (root.TryGetProperty("amount", out var amt))
            (refundAmount, curr) = ReadMoney(amt);

        return new PayPalRefundResult
        {
            RefundId = root.GetProperty("id").GetString()!,
            Status = root.GetProperty("status").GetString() ?? "UNKNOWN",
            Amount = refundAmount,
            Currency = curr
        };
    }

    // -------------------- Vault --------------------

    public async Task<PayPalVaultCardResult> VaultCardAsync(PayPalCardDetails card, CancellationToken cancellationToken = default)
    {
        var body = new { payment_source = new { card = BuildCardNode(card) } };

        using var doc = await SendJsonAsync(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            body,
            cancellationToken,
            headers: h => h.TryAddWithoutValidation("PayPal-Request-Id", Guid.NewGuid().ToString("N")));

        var root = doc!.RootElement;
        var vaultId = root.GetProperty("id").GetString()!;

        string? last4 = null, brand = null, expiry = null, name = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var c))
        {
            last4 = GetStringOrNull(c, "last_digits");
            brand = GetStringOrNull(c, "brand");
            expiry = GetStringOrNull(c, "expiry");
            name = GetStringOrNull(c, "name");
        }

        return new PayPalVaultCardResult { VaultId = vaultId, Last4 = last4, Brand = brand, Expiry = expiry, Name = name };
    }

    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendJsonAsync(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}",
            body: null,
            cancellationToken);
    }

    // -------------------- Transaction Search --------------------

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();

        // Chunk the span into <=31-day windows, and page through each window fully.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.Add(MaxSearchWindow);
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            int totalPages;
            do
            {
                var url = "/v1/reporting/transactions" +
                          $"?start_date={Uri.EscapeDataString(FormatSearchDate(windowStart))}" +
                          $"&end_date={Uri.EscapeDataString(FormatSearchDate(windowEnd))}" +
                          "&fields=transaction_info" +
                          $"&page_size={SearchPageSize}&page={page}";

                using var doc = await SendJsonAsync(HttpMethod.Get, url, body: null, cancellationToken);
                var root = doc!.RootElement;

                totalPages = root.TryGetProperty("total_pages", out var tp) && tp.ValueKind == JsonValueKind.Number ? tp.GetInt32() : 1;

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (!detail.TryGetProperty("transaction_info", out var info)) continue;

                        decimal amount = 0m; string? currency = null;
                        if (info.TryGetProperty("transaction_amount", out var ta))
                            (amount, currency) = ReadMoney(ta);

                        DateTimeOffset? date = info.TryGetProperty("transaction_initiation_date", out var d) && d.ValueKind == JsonValueKind.String
                            ? DateTimeOffset.Parse(d.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                            : null;

                        results.Add(new PayPalTransaction
                        {
                            TransactionId = GetStringOrNull(info, "transaction_id") ?? string.Empty,
                            InvoiceId = GetStringOrNull(info, "invoice_id"),
                            Status = GetStringOrNull(info, "transaction_status"),
                            EventCode = GetStringOrNull(info, "transaction_event_code"),
                            Amount = amount,
                            Currency = currency,
                            InitiationDate = date
                        });
                    }
                }

                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd;
        }

        return results;
    }

    // -------------------- HTTP plumbing --------------------

    private async Task<JsonDocument?> SendJsonAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken,
        Action<HttpRequestHeaders>? headers = null)
    {
        // One retry after forcing a fresh token, to survive a token that expired early.
        for (var attempt = 0; ; attempt++)
        {
            var token = await GetAccessTokenAsync(forceRefresh: attempt > 0, cancellationToken);

            using var request = new HttpRequestMessage(method, BaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            headers?.Invoke(request.Headers);

            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _logger.LogWarning("PayPal returned 401; refreshing access token and retrying once.");
                continue;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw MapError(response.StatusCode, payload, method, path);

            if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(payload))
                return null;

            return JsonDocument.Parse(payload);
        }
    }

    private async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
            return cached!;

        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
            throw new PayPalApiException("PayPal credentials are not configured (PayPal:ClientId / PayPal:ClientSecret).", 500);

        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/v1/oauth2/token");
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });

        using var response = await _http.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw MapError(response.StatusCode, payload, HttpMethod.Post, "/v1/oauth2/token");

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var accessToken = root.GetProperty("access_token").GetString()!;
        var expiresIn = root.TryGetProperty("expires_in", out var ei) && ei.ValueKind == JsonValueKind.Number ? ei.GetInt32() : 3600;

        // Cache with a safety margin so we refresh before PayPal expires the token.
        var lifetime = TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60));
        _cache.Set(TokenCacheKey, accessToken, lifetime);
        return accessToken;
    }

    private Exception MapError(HttpStatusCode statusCode, string payload, HttpMethod method, string path)
    {
        string? name = null, message = null, debugId = null;
        var issues = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            name = GetStringOrNull(root, "name");
            message = GetStringOrNull(root, "message") ?? GetStringOrNull(root, "error_description");
            debugId = GetStringOrNull(root, "debug_id");
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in details.EnumerateArray())
                {
                    var issue = GetStringOrNull(d, "issue");
                    if (!string.IsNullOrEmpty(issue)) issues.Add(issue!);
                }
            }
        }
        catch (JsonException)
        {
            message = payload;
        }

        var summary = $"PayPal {method} {path} failed with {(int)statusCode} ({name ?? "error"}): {message}" +
                      (issues.Count > 0 ? $" [issues: {string.Join(", ", issues)}]" : string.Empty) +
                      (debugId is not null ? $" [debug_id: {debugId}]" : string.Empty);

        _logger.LogError("{Summary}", summary);

        // Card declines and denials surface as a distinct, shopper-actionable failure.
        var declineIssues = new[] { "INSTRUMENT_DECLINED", "PAYMENT_DENIED", "TRANSACTION_REFUSED", "CARD_EXPIRED", "PAYER_ACCOUNT_RESTRICTED" };
        if (issues.Any(i => declineIssues.Contains(i, StringComparer.OrdinalIgnoreCase)))
            return new PaymentDeclinedException($"The card payment was declined by PayPal: {string.Join(", ", issues)}.");

        return new PayPalApiException(summary, (int)statusCode, debugId, issues);
    }

    // -------------------- helpers --------------------

    private static object BuildCardNode(PayPalCardDetails card)
    {
        var node = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["name"] = card.Name
        };

        if (card.BillingAddress is not null)
        {
            var a = card.BillingAddress;
            node["billing_address"] = new Dictionary<string, object?>
            {
                ["address_line_1"] = a.AddressLine1,
                ["address_line_2"] = a.AddressLine2,
                ["admin_area_2"] = a.AdminArea2,
                ["admin_area_1"] = a.AdminArea1,
                ["postal_code"] = a.PostalCode,
                ["country_code"] = a.CountryCode
            };
        }

        return node;
    }

    private static JsonElement? FindFirstPayment(JsonElement orderRoot, string collectionName)
    {
        if (!orderRoot.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments) &&
                payments.TryGetProperty(collectionName, out var coll) &&
                coll.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in coll.EnumerateArray())
                    return entry;
            }
        }

        return null;
    }

    private static (decimal amount, string currency) ReadMoney(JsonElement money)
    {
        var value = money.TryGetProperty("value", out var v) ? v.GetString() : null;
        var currency = money.TryGetProperty("currency_code", out var c) ? c.GetString() : null;
        var amount = value is not null ? decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture) : 0m;
        return (amount, currency ?? string.Empty);
    }

    private static string? GetStringOrNull(JsonElement element, string property) =>
        element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string FormatAmount(decimal amount, string currency)
    {
        var decimals = DecimalsFor(currency);
        return decimal.Round(amount, decimals, MidpointRounding.AwayFromZero)
            .ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    // Decimal places per PayPal currency rules; the common zero/three-decimal exceptions plus a 2-decimal default.
    private static int DecimalsFor(string currency) => currency.ToUpperInvariant() switch
    {
        "JPY" or "HUF" or "TWD" => 0,
        _ => 2
    };

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}

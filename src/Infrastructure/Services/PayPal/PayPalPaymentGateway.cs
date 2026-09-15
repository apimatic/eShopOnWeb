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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// PayPal REST implementation of <see cref="IPaymentGateway"/>. Talks to the Orders v2, Payments
/// v2, Payment-Method-Tokens v3 and Transaction-Search v1 APIs over plain HTTP. Full card details
/// are only ever forwarded to PayPal — never persisted and never written to logs.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private const string AccessTokenCacheKey = "paypal.access_token";
    private static readonly SemaphoreSlim TokenLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(HttpClient http, PayPalSettings settings, IMemoryCache cache,
        IAppLogger<PayPalPaymentGateway> logger)
    {
        _http = http;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    private string BaseUrl => _settings.ResolvedBaseUrl;

    // ---------------------------------------------------------------- Authorize (hold)

    public async Task<AuthorizationResult> AuthorizeAsync(PaymentAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        object card = request.VaultId is not null
            ? new { vault_id = request.VaultId }
            : new
            {
                number = request.Card!.Number,
                expiry = request.Card.Expiry,
                security_code = request.Card.SecurityCode,
                name = request.Card.CardholderName,
                billing_address = ToPayPalAddress(request.Card.BillingAddress)
            };

        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    invoice_id = request.InvoiceId,
                    custom_id = request.CustomId,
                    amount = Money(request.Amount, request.Currency)
                }
            },
            payment_source = new { card }
        };

        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = request.IdempotencyKey,
            ["Prefer"] = "return=representation"
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, headers, cancellationToken);
        var root = doc!.RootElement;

        var payPalOrderId = GetString(root, "id") ?? throw new PaymentException("PayPal did not return an order id.");
        var status = GetString(root, "status") ?? "UNKNOWN";

        if (RequiresBrowserApproval(root, status))
            return new AuthorizationResult(payPalOrderId, string.Empty, status, null, RequiresBrowserApproval: true);

        if (TryReadAuthorization(root, out var authId, out var authStatus, out var expiresAt))
            return new AuthorizationResult(payPalOrderId, authId!, authStatus!, expiresAt, false);

        // The card was attached but not yet authorized inline; authorize the order explicitly.
        var authHeaders = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = request.IdempotencyKey + "-authorize",
            ["Prefer"] = "return=representation"
        };
        using var authDoc = await SendAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{payPalOrderId}/authorize", new { }, authHeaders, cancellationToken);
        var authRoot = authDoc!.RootElement;

        var authStatus2 = GetString(authRoot, "status") ?? "UNKNOWN";
        if (RequiresBrowserApproval(authRoot, authStatus2))
            return new AuthorizationResult(payPalOrderId, string.Empty, authStatus2, null, RequiresBrowserApproval: true);

        if (!TryReadAuthorization(authRoot, out authId, out authStatus, out expiresAt))
            throw new PaymentException("PayPal accepted the order but returned no authorization to act on.");

        return new AuthorizationResult(payPalOrderId, authId!, authStatus!, expiresAt, false);
    }

    // ---------------------------------------------------------------- Authorization state

    public async Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        var root = doc!.RootElement;
        return new AuthorizationSnapshot(
            GetString(root, "id") ?? authorizationId,
            GetString(root, "status") ?? "UNKNOWN",
            GetDate(root, "expiration_time"));
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, CancellationToken cancellationToken = default)
    {
        var body = new { amount = Money(amount, currency) };
        var headers = new Dictionary<string, string> { ["Prefer"] = "return=representation" };
        using var doc = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, headers, cancellationToken);
        var root = doc!.RootElement;
        return new AuthorizationResult(
            GetString(root, "id") ?? authorizationId,
            GetString(root, "id") ?? authorizationId,
            GetString(root, "status") ?? "UNKNOWN",
            GetDate(root, "expiration_time"),
            false);
    }

    // ---------------------------------------------------------------- Capture (take)

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = Money(amount, currency),
            invoice_id = invoiceId,
            final_capture = true
        };
        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = idempotencyKey,
            ["Prefer"] = "return=representation"
        };

        using var doc = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", body, headers, cancellationToken);
        var root = doc!.RootElement;

        var captureId = GetString(root, "id") ?? throw new PaymentException("PayPal did not return a capture id.");
        var status = GetString(root, "status") ?? "UNKNOWN";

        decimal captured = amount;
        decimal? fee = null;
        decimal? net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            captured = GetMoney(breakdown, "gross_amount") ?? amount;
            fee = GetMoney(breakdown, "paypal_fee");
            net = GetMoney(breakdown, "net_amount");
        }

        return new CaptureResult(captureId, status, captured, fee, net);
    }

    // ---------------------------------------------------------------- Void (release)

    public async Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void", null, null, cancellationToken);
    }

    // ---------------------------------------------------------------- Refund (return)

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object body = amount is not null
            ? new { amount = Money(amount.Value, currency) }
            : new { };
        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = idempotencyKey,
            ["Prefer"] = "return=representation"
        };

        using var doc = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund", body, headers, cancellationToken);
        var root = doc!.RootElement;

        var refundId = GetString(root, "id") ?? throw new PaymentException("PayPal did not return a refund id.");
        var status = GetString(root, "status") ?? "UNKNOWN";
        var refunded = GetMoney(root, "amount") ?? amount ?? 0m;
        return new RefundResult(refundId, status, refunded);
    }

    // ---------------------------------------------------------------- Vault a card

    public async Task<VaultCardResult> VaultCardAsync(CardDetails card, string? customerId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Step 1: create a setup token from the raw card. No verification_method is requested, so
        // an ordinary card is approved immediately (no browser step); a card whose issuer forces
        // SCA comes back needing payer action, which we surface as a browser-approval challenge.
        var setupBody = new
        {
            payment_source = new
            {
                card = new
                {
                    number = card.Number,
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    name = card.CardholderName,
                    billing_address = ToPayPalAddress(card.BillingAddress)
                }
            },
            customer = customerId is null ? null : new { id = customerId }
        };
        var setupHeaders = new Dictionary<string, string> { ["PayPal-Request-Id"] = idempotencyKey };

        using var setupDoc = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens",
            setupBody, setupHeaders, cancellationToken);
        var setupRoot = setupDoc!.RootElement;

        var setupTokenId = GetString(setupRoot, "id") ?? throw new PaymentException("PayPal did not return a setup token id.");
        var setupStatus = GetString(setupRoot, "status") ?? "UNKNOWN";
        if (RequiresBrowserApproval(setupRoot, setupStatus))
            return new VaultCardResult(string.Empty, customerId, string.Empty, string.Empty, card.Expiry, RequiresBrowserApproval: true);

        // Step 2: exchange the approved setup token for a permanent payment token.
        var tokenBody = new
        {
            payment_source = new
            {
                token = new { id = setupTokenId, type = "SETUP_TOKEN" }
            }
        };
        var tokenHeaders = new Dictionary<string, string> { ["PayPal-Request-Id"] = idempotencyKey + "-token" };

        using var tokenDoc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens",
            tokenBody, tokenHeaders, cancellationToken);
        var tokenRoot = tokenDoc!.RootElement;

        var vaultId = GetString(tokenRoot, "id") ?? throw new PaymentException("PayPal did not return a vault token id.");
        var resolvedCustomerId = customerId;
        if (tokenRoot.TryGetProperty("customer", out var customer))
            resolvedCustomerId = GetString(customer, "id") ?? resolvedCustomerId;

        string brand = string.Empty, last4 = string.Empty, expiry = card.Expiry;
        if (tokenRoot.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            brand = GetString(cardEl, "brand") ?? string.Empty;
            last4 = GetString(cardEl, "last_digits") ?? string.Empty;
            expiry = GetString(cardEl, "expiry") ?? card.Expiry;
        }

        return new VaultCardResult(vaultId, resolvedCustomerId, brand, last4, expiry, RequiresBrowserApproval: false);
    }

    public async Task DeleteVaultTokenAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}",
            null, null, cancellationToken);
    }

    // ---------------------------------------------------------------- Reconciliation

    public async Task<IReadOnlyCollection<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<GatewayTransaction>();

        // PayPal's transaction search allows at most a 31-day window; cover the whole range in chunks.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            int totalPages;
            do
            {
                var query = "/v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(FormatDate(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatDate(windowEnd))}" +
                    $"&fields=all&page_size=500&page={page}";

                using var doc = await SendAsync(HttpMethod.Get, query, null, null, cancellationToken);
                var root = doc!.RootElement;

                totalPages = root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var tpv) ? tpv : 1;

                if (root.TryGetProperty("transaction_details", out var details) &&
                    details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (!detail.TryGetProperty("transaction_info", out var info))
                            continue;

                        var txId = GetString(info, "transaction_id");
                        if (txId is null) continue;

                        decimal amount = 0m;
                        string currency = _settings.Currency;
                        if (info.TryGetProperty("transaction_amount", out var amt))
                        {
                            amount = ParseDecimal(GetString(amt, "value")) ?? 0m;
                            currency = GetString(amt, "currency_code") ?? currency;
                        }

                        results.Add(new GatewayTransaction(
                            TransactionId: txId,
                            Status: GetString(info, "transaction_status") ?? "UNKNOWN",
                            Amount: amount,
                            Currency: currency,
                            InvoiceId: GetString(info, "invoice_id"),
                            CustomField: GetString(info, "custom_field"),
                            InitiatedAt: GetDate(info, "transaction_initiation_date")));
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

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(AccessTokenCacheKey, out string? cached) && cached is not null)
            return cached;

        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(AccessTokenCacheKey, out cached) && cached is not null)
                return cached;

            using var message = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            message.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8,
                "application/x-www-form-urlencoded");

            using var response = await _http.SendAsync(message, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new PaymentException($"PayPal token request failed (http {(int)response.StatusCode}): {ExtractError(payload)}");

            using var doc = JsonDocument.Parse(payload);
            var token = GetString(doc.RootElement, "access_token")
                ?? throw new PaymentException("PayPal token response contained no access_token.");
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? s : 300;

            _cache.Set(AccessTokenCacheKey, token, TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60)));
            return token;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string pathOrUrl, object? body,
        IDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        var url = pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? pathOrUrl : $"{BaseUrl}{pathOrUrl}";
        var token = await GetAccessTokenAsync(cancellationToken);

        using var message = new HttpRequestMessage(method, url);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (headers is not null)
        {
            foreach (var (key, value) in headers)
                message.Headers.TryAddWithoutValidation(key, value);
        }
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(message, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        // Never log request bodies (they may carry card data). Log only method, path and status.
        _logger.LogInformation($"PayPal {method} {Sanitize(pathOrUrl)} -> {(int)response.StatusCode}");

        if (!response.IsSuccessStatusCode)
            throw new PaymentException(ExtractError(payload, (int)response.StatusCode));

        if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(payload))
            return JsonDocument.Parse("{}");

        return JsonDocument.Parse(payload);
    }

    // ---------------------------------------------------------------- JSON helpers

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency,
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static object? ToPayPalAddress(BillingAddress? address)
    {
        if (address is null) return null;
        return new
        {
            address_line_1 = address.Line1,
            address_line_2 = address.Line2,
            admin_area_2 = address.City,
            admin_area_1 = address.State,
            postal_code = address.PostalCode,
            country_code = address.CountryCode
        };
    }

    private static bool TryReadAuthorization(JsonElement root, out string? id, out string? status,
        out DateTimeOffset? expiresAt)
    {
        id = null; status = null; expiresAt = null;
        if (root.TryGetProperty("purchase_units", out var units) && units.ValueKind == JsonValueKind.Array)
        {
            foreach (var unit in units.EnumerateArray())
            {
                if (unit.TryGetProperty("payments", out var payments) &&
                    payments.TryGetProperty("authorizations", out var auths) &&
                    auths.ValueKind == JsonValueKind.Array && auths.GetArrayLength() > 0)
                {
                    var first = auths[0];
                    id = GetString(first, "id");
                    status = GetString(first, "status");
                    expiresAt = GetDate(first, "expiration_time");
                    return id is not null;
                }
            }
        }
        return false;
    }

    private static bool RequiresBrowserApproval(JsonElement root, string status)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            return true;
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                var rel = GetString(link, "rel");
                if (string.Equals(rel, "payer-action", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rel, "approve", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? GetMoney(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var money) ? ParseDecimal(GetString(money, "value")) : null;

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTimeOffset? GetDate(JsonElement element, string property) =>
        GetString(element, property) is { } s &&
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
            ? dto
            : null;

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);

    // Strip ids from the logged path so nothing sensitive or noisy leaks; keep the shape.
    private static string Sanitize(string path)
    {
        var q = path.IndexOf('?');
        return q >= 0 ? path[..q] : path;
    }

    private static string ExtractError(string payload, int? httpStatus = null)
    {
        var prefix = httpStatus is not null ? $"PayPal request failed (http {httpStatus}): " : string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var name = GetString(root, "name") ?? GetString(root, "error");
            var message = GetString(root, "message") ?? GetString(root, "error_description");
            var debugId = GetString(root, "debug_id");

            var detail = string.Empty;
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array &&
                details.GetArrayLength() > 0)
            {
                var first = details[0];
                detail = $" [{GetString(first, "issue")}: {GetString(first, "description")}]";
            }

            var body = $"{name}: {message}{detail}";
            if (debugId is not null) body += $" (debug_id={debugId})";
            return prefix + (string.IsNullOrWhiteSpace(body) ? payload : body);
        }
        catch
        {
            return prefix + payload;
        }
    }
}

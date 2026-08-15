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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Talks to the PayPal REST API over plain HTTP. Handles OAuth token caching, request idempotency and
/// defensive parsing. Raw card details flow through only in-memory request bodies and are never logged.
/// </summary>
public class PayPalPaymentService : IPayPalPaymentService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private const string TokenCacheKey = "PayPal:AccessToken";
    private static readonly SemaphoreSlim TokenLock = new(1, 1);

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalPaymentService> _logger;
    private readonly IMemoryCache _cache;
    private readonly string _baseUrl;

    public PayPalPaymentService(HttpClient httpClient, IOptions<PayPalSettings> settings,
        IAppLogger<PayPalPaymentService> logger, IMemoryCache cache)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _cache = cache;
        _settings.Validate();
        _baseUrl = _settings.ResolveBaseUrl();
    }

    public string Currency => _settings.Currency!;

    // ----------------------------------------------------------------- Authorize (hold)

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeRequest request,
        CancellationToken cancellationToken = default)
    {
        object paymentSource;
        if (!string.IsNullOrEmpty(request.VaultId))
        {
            paymentSource = new { card = new { vault_id = request.VaultId } };
        }
        else if (request.Card != null)
        {
            paymentSource = new { card = BuildCardBody(request.Card, request.StoreInVault) };
        }
        else
        {
            throw new PaymentException("A PayPal authorization needs either a card or a saved-card vault id.");
        }

        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    invoice_id = request.ReconciliationId,
                    custom_id = request.ReconciliationId,
                    amount = new { currency_code = request.Currency, value = Format(request.Amount) }
                }
            },
            payment_source = paymentSource
        };

        using var response = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body,
            requestId: request.RequestId, preferRepresentation: true, cancellationToken);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        var root = doc!.RootElement;

        var orderStatus = GetString(root, "status") ?? "UNKNOWN";
        var requiresApproval = orderStatus == "PAYER_ACTION_REQUIRED" || HasPayerActionLink(root);

        string? authId = null, authStatus = null;
        DateTimeOffset? expiresAt = null;
        if (TryGetFirstAuthorization(root, out var auth))
        {
            authId = GetString(auth, "id");
            authStatus = GetString(auth, "status");
            expiresAt = GetDate(auth, "expiration_time");
        }

        string? brand = null, last4 = null, vaultId = null, vaultCustomerId = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var card))
        {
            brand = GetString(card, "brand");
            last4 = GetString(card, "last_digits");
            if (card.TryGetProperty("attributes", out var attrs) && attrs.TryGetProperty("vault", out var vault))
            {
                vaultId = GetString(vault, "id");
                if (vault.TryGetProperty("customer", out var cust))
                {
                    vaultCustomerId = GetString(cust, "id");
                }
            }
        }

        return new PayPalAuthorizationResult
        {
            PayPalOrderId = GetString(root, "id") ?? throw new PayPalApiException("PayPal order response had no id."),
            OrderStatus = orderStatus,
            AuthorizationId = authId,
            AuthorizationStatus = authStatus,
            ExpiresAt = expiresAt,
            CardBrand = brand,
            CardLast4 = last4,
            VaultId = vaultId,
            VaultCustomerId = vaultCustomerId,
            RequiresApproval = requiresApproval,
            ApprovalUrl = requiresApproval ? GetLinkHref(root, "payer-action") : null
        };
    }

    // ----------------------------------------------------------------- Capture (take)

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new { final_capture = true };
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", body,
            requestId: requestId, preferRepresentation: true, cancellationToken);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        var root = doc!.RootElement;

        decimal amount = 0m;
        string currency = Currency;
        if (root.TryGetProperty("amount", out var amt))
        {
            amount = GetDecimal(amt, "value") ?? 0m;
            currency = GetString(amt, "currency_code") ?? currency;
        }

        decimal? fee = null, net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var srb))
        {
            fee = srb.TryGetProperty("paypal_fee", out var f) ? GetDecimal(f, "value") : null;
            net = srb.TryGetProperty("net_amount", out var n) ? GetDecimal(n, "value") : null;
        }

        return new PayPalCaptureResult(
            GetString(root, "id") ?? throw new PayPalApiException("Capture response had no id."),
            GetString(root, "status") ?? "UNKNOWN", amount, fee, net, currency);
    }

    // ----------------------------------------------------------------- Reauthorize (renew)

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new { amount = new { currency_code = currency, value = Format(amount) } };
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", body,
            requestId: requestId, preferRepresentation: true, cancellationToken);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        var root = doc!.RootElement;

        return new PayPalAuthorizationResult
        {
            PayPalOrderId = string.Empty,
            OrderStatus = "REAUTHORIZED",
            AuthorizationId = GetString(root, "id"),
            AuthorizationStatus = GetString(root, "status"),
            ExpiresAt = GetDate(root, "expiration_time")
        };
    }

    // ----------------------------------------------------------------- Void (release)

    public async Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void", body: null,
            requestId: null, preferRepresentation: false, cancellationToken);
        // 204 No Content on success; SendAsync already threw for error statuses.
    }

    public async Task<PayPalAuthorizationInfo> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}", body: null,
            requestId: null, preferRepresentation: false, cancellationToken);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        var root = doc!.RootElement;
        return new PayPalAuthorizationInfo(GetString(root, "status") ?? "UNKNOWN", GetDate(root, "expiration_time"));
    }

    // ----------------------------------------------------------------- Refund (return)

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object? body = amount.HasValue
            ? new { amount = new { currency_code = currency, value = Format(amount.Value) } }
            : null;

        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund", body,
            requestId: idempotencyKey, preferRepresentation: true, cancellationToken);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        var root = doc!.RootElement;

        decimal refunded = amount ?? 0m;
        string cur = currency;
        if (root.TryGetProperty("amount", out var amt))
        {
            refunded = GetDecimal(amt, "value") ?? refunded;
            cur = GetString(amt, "currency_code") ?? cur;
        }

        return new PayPalRefundResult(
            GetString(root, "id") ?? throw new PayPalApiException("Refund response had no id."),
            GetString(root, "status") ?? "UNKNOWN", refunded, cur);
    }

    // ----------------------------------------------------------------- Vault (save card)

    public async Task<PayPalVaultedCard> VaultCardAsync(PayPalCard card, string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new { payment_source = new { card = BuildCardBody(card, storeInVault: false) } };
        using var response = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body,
            requestId: requestId, preferRepresentation: false, cancellationToken);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        var root = doc!.RootElement;

        var vaultId = GetString(root, "id") ?? throw new PayPalApiException("Vault response had no token id.");
        string? customerId = root.TryGetProperty("customer", out var cust) ? GetString(cust, "id") : null;

        string brand = "CARD", last4 = "0000", expiry = card.Expiry;
        string? name = card.Name;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var c))
        {
            brand = GetString(c, "brand") ?? brand;
            last4 = GetString(c, "last_digits") ?? last4;
            expiry = GetString(c, "expiry") ?? expiry;
            name = GetString(c, "name") ?? name;
        }

        return new PayPalVaultedCard(vaultId, customerId, brand, last4, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}",
            body: null, requestId: null, preferRepresentation: false, cancellationToken);
        // 204 No Content on success; SendAsync already threw for error statuses.
    }

    // ----------------------------------------------------------------- Reconciliation

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var results = new List<PayPalTransaction>();

        // PayPal caps each reporting request at a 31-day window, so walk the range in chunks.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            try
            {
                await ReadTransactionWindowAsync(windowStart, windowEnd, results, cancellationToken);
            }
            catch (PayPalApiException ex) when (IsReportingDataUnavailable(ex))
            {
                // PayPal's reporting lags live activity: a window whose data has not settled yet comes
                // back as "data not available". That is a legitimately empty window, not a failure.
                _logger.LogInformation(
                    $"PayPal reporting has no settled data yet for {FormatDate(windowStart)}..{FormatDate(windowEnd)}; treating as empty.");
            }

            windowStart = windowEnd;
        }

        return results;
    }

    /// <summary>True for the reporting "data not available yet" response caused by the reporting lag.</summary>
    private static bool IsReportingDataUnavailable(PayPalApiException ex) =>
        ex.HttpStatus == 404 &&
        (ex.Message.Contains("not available", StringComparison.OrdinalIgnoreCase) ||
         ex.Message.Contains("given start date", StringComparison.OrdinalIgnoreCase));

    private async Task ReadTransactionWindowAsync(DateTimeOffset start, DateTimeOffset end,
        List<PayPalTransaction> sink, CancellationToken cancellationToken)
    {
        var page = 1;
        var totalPages = 1;
        do
        {
            var path = $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(FormatDate(start))}" +
                       $"&end_date={Uri.EscapeDataString(FormatDate(end))}&fields=all&page_size=500&page={page}";

            using var response = await SendAsync(HttpMethod.Get, path, body: null, requestId: null,
                preferRepresentation: false, cancellationToken);
            using var doc = await ReadJsonAsync(response, cancellationToken);
            var root = doc!.RootElement;

            if (root.TryGetProperty("total_pages", out var tp) && tp.ValueKind == JsonValueKind.Number)
            {
                totalPages = tp.GetInt32();
            }

            if (root.TryGetProperty("transaction_details", out var details) &&
                details.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in details.EnumerateArray())
                {
                    if (!d.TryGetProperty("transaction_info", out var info))
                    {
                        continue;
                    }

                    decimal? amount = null;
                    string? currency = null;
                    if (info.TryGetProperty("transaction_amount", out var ta))
                    {
                        amount = GetDecimal(ta, "value");
                        currency = GetString(ta, "currency_code");
                    }

                    sink.Add(new PayPalTransaction(
                        GetString(info, "transaction_id") ?? string.Empty,
                        GetString(info, "transaction_status") ?? "UNKNOWN",
                        amount,
                        currency,
                        GetString(info, "invoice_id"),
                        GetString(info, "custom_field"),
                        GetDate(info, "transaction_initiation_date"),
                        GetString(info, "transaction_event_code")));
                }
            }

            page++;
        }
        while (page <= totalPages);
    }

    // ----------------------------------------------------------------- HTTP plumbing

    private const int MaxAttempts = 3;

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body,
        string? requestId, bool preferRepresentation, CancellationToken cancellationToken)
    {
        // Serialize the body once so a retry replays the identical request (idempotent via PayPal-Request-Id).
        var payload = body == null ? null : JsonSerializer.Serialize(body, JsonOptions);

        for (var attempt = 1; ; attempt++)
        {
            var token = await GetAccessTokenAsync(cancellationToken);

            using var request = new HttpRequestMessage(method, _baseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (!string.IsNullOrEmpty(requestId))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }
            if (preferRepresentation)
            {
                request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            }
            if (payload != null)
            {
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await Task.Delay(200 * attempt, cancellationToken);
                continue;
            }

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            // Retry transient server-side / throttling responses; every mutating call is idempotent.
            if (IsTransient(response.StatusCode) && attempt < MaxAttempts)
            {
                response.Dispose();
                await Task.Delay(200 * attempt, cancellationToken);
                continue;
            }

            await ThrowApiExceptionAsync(response, path, cancellationToken);
            return response; // unreachable; ThrowApiExceptionAsync always throws
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        (int)status >= 500 || status == HttpStatusCode.TooManyRequests;

    private async Task ThrowApiExceptionAsync(HttpResponseMessage response, string path, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        string? issue = null, description = null, debugId = null, name = null;
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
            var root = doc.RootElement;
            name = GetString(root, "name");
            debugId = GetString(root, "debug_id");
            description = GetString(root, "message");
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array &&
                details.GetArrayLength() > 0)
            {
                var first = details[0];
                issue = GetString(first, "issue");
                description = GetString(first, "description") ?? description;
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with what we have.
        }

        var message = $"PayPal {path} failed ({status}{(name != null ? $" {name}" : string.Empty)}" +
                      $"{(issue != null ? $"/{issue}" : string.Empty)}): {description ?? "no detail"}.";
        _logger.LogWarning(message + (debugId != null ? $" debug_id={debugId}" : string.Empty));
        response.Dispose();
        throw new PayPalApiException(message, status, issue, debugId);
    }

    private async Task<JsonDocument?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return JsonDocument.Parse("{}");
        }
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<string>(TokenCacheKey, out var cached) && cached != null)
        {
            return cached;
        }

        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue<string>(TokenCacheKey, out var existing) && existing != null)
            {
                return existing;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowApiExceptionAsync(response, "/v1/oauth2/token", cancellationToken);
            }

            using var doc = await ReadJsonAsync(response, cancellationToken);
            var root = doc!.RootElement;
            var token = GetString(root, "access_token")
                ?? throw new PayPalApiException("PayPal token response had no access_token.");
            var expiresIn = root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number
                ? e.GetInt32() : 300;

            // Refresh a minute early to avoid using a token that expires mid-flight.
            _cache.Set(TokenCacheKey, token, TimeSpan.FromSeconds(Math.Max(30, expiresIn - 60)));
            return token;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    // ----------------------------------------------------------------- helpers

    private static object BuildCardBody(PayPalCard card, bool storeInVault)
    {
        var body = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode
        };
        if (!string.IsNullOrWhiteSpace(card.Name))
        {
            body["name"] = card.Name;
        }
        if (card.BillingAddress != null)
        {
            var a = card.BillingAddress;
            var address = new Dictionary<string, object?>();
            AddIfPresent(address, "address_line_1", a.Line1);
            AddIfPresent(address, "address_line_2", a.Line2);
            AddIfPresent(address, "admin_area_2", a.City);
            AddIfPresent(address, "admin_area_1", a.State);
            AddIfPresent(address, "postal_code", a.PostalCode);
            AddIfPresent(address, "country_code", a.CountryCode);
            if (address.Count > 0)
            {
                body["billing_address"] = address;
            }
        }

        var attributes = new Dictionary<string, object?>
        {
            ["verification"] = new { method = "SCA_WHEN_REQUIRED" }
        };
        if (storeInVault)
        {
            attributes["vault"] = new { store_in_vault = "ON_SUCCESS" };
        }
        body["attributes"] = attributes;

        return body;
    }

    private static void AddIfPresent(Dictionary<string, object?> map, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            map[key] = value;
        }
    }

    private static bool TryGetFirstAuthorization(JsonElement root, out JsonElement authorization)
    {
        authorization = default;
        if (root.TryGetProperty("purchase_units", out var units) && units.ValueKind == JsonValueKind.Array)
        {
            foreach (var unit in units.EnumerateArray())
            {
                if (unit.TryGetProperty("payments", out var payments) &&
                    payments.TryGetProperty("authorizations", out var auths) &&
                    auths.ValueKind == JsonValueKind.Array && auths.GetArrayLength() > 0)
                {
                    authorization = auths[0];
                    return true;
                }
            }
        }
        return false;
    }

    private static bool HasPayerActionLink(JsonElement root) => GetLinkHref(root, "payer-action") != null;

    private static string? GetLinkHref(JsonElement root, string rel)
    {
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                if (string.Equals(GetString(link, "rel"), rel, StringComparison.OrdinalIgnoreCase))
                {
                    return GetString(link, "href");
                }
            }
        }
        return null;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? GetDecimal(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }
        return null;
    }

    private static DateTimeOffset? GetDate(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
        {
            return dt;
        }
        return null;
    }

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

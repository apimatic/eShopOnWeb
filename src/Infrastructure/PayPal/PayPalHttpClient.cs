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
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// A hand-written client for the PayPal REST APIs, built directly to the OpenAPI specifications in
/// <c>api-specs/</c>: Orders v2 (create/authorize), Payments v2 (capture/reauthorize/void/refund),
/// Vault v3 (payment tokens) and Transaction Search v1 (reconciliation). No third-party PayPal SDK
/// is used. Access tokens are obtained via the OAuth2 client-credentials flow described by the specs'
/// security scheme and cached until shortly before they expire.
/// </summary>
public sealed class PayPalHttpClient : IPayPalClient
{
    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalHttpClient> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public PayPalHttpClient(HttpClient http, IOptions<PayPalSettings> settings, IAppLogger<PayPalHttpClient> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
    }

    private string BaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                return _settings.BaseUrl.TrimEnd('/');
            }
            // Derive from the environment when no explicit override is configured.
            var env = (_settings.Environment ?? "sandbox").Trim().ToLowerInvariant();
            return env is "live" or "production"
                ? "https://api-m.paypal.com"
                : "https://api-m.sandbox.paypal.com";
        }
    }

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    // --- OAuth token ---

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });

            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw ToException((int)response.StatusCode, body);
            }

            var token = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions)
                ?? throw new PayPalApiException((int)response.StatusCode, "TOKEN_PARSE_ERROR", "Could not parse the PayPal token response.", null, Array.Empty<string>());

            _accessToken = token.AccessToken;
            // Refresh a minute before expiry to avoid using a token that lapses mid-request.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, token.ExpiresIn - 60));
            return _accessToken!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    // --- core send ---

    private async Task<(int status, string body)> SendAsync(
        HttpMethod method, string path, object? payload, string? requestId, bool preferRepresentation, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);
        using var request = new HttpRequestMessage(method, $"{BaseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (payload is not null)
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw ToException((int)response.StatusCode, body);
        }
        return ((int)response.StatusCode, body);
    }

    private static PayPalApiException ToException(int status, string body)
    {
        string? name = null, message = null, debugId = null;
        var issues = new List<string>();
        try
        {
            var err = JsonSerializer.Deserialize<ErrorResponseDto>(body, JsonOptions);
            if (err is not null)
            {
                name = err.Name ?? err.Error;
                message = err.Message ?? err.ErrorDescription;
                debugId = err.DebugId;
                if (err.Details is not null)
                {
                    issues.AddRange(err.Details.Select(d => $"{d.Issue}{(string.IsNullOrEmpty(d.Description) ? "" : ": " + d.Description)}"));
                }
            }
        }
        catch (JsonException) { /* non-JSON error body */ }

        message ??= $"PayPal request failed with HTTP {status}.";
        return new PayPalApiException(status, name, message, debugId, issues);
    }

    // --- helpers for building request bodies ---

    private object BuildCardObject(PayPalCardDetails card)
    {
        object? billingAddress = null;
        if (!string.IsNullOrWhiteSpace(card.AddressLine1) || !string.IsNullOrWhiteSpace(card.PostalCode))
        {
            billingAddress = new
            {
                address_line_1 = card.AddressLine1,
                address_line_2 = card.AddressLine2,
                admin_area_2 = card.AdminArea2,
                admin_area_1 = card.AdminArea1,
                postal_code = card.PostalCode,
                country_code = string.IsNullOrWhiteSpace(card.CountryCode) ? "US" : card.CountryCode
            };
        }
        return new
        {
            name = card.Name,
            number = card.Number,
            expiry = card.Expiry,
            security_code = card.SecurityCode,
            billing_address = billingAddress
        };
    }

    // --- Orders v2 ---

    public async Task<PayPalOrderResult> CreateAuthorizeOrderAsync(
        decimal amount, string currency, string invoiceId, string customId, string idempotencyKey, CancellationToken ct = default)
    {
        var payload = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = customId,
                    invoice_id = invoiceId,
                    custom_id = customId,
                    amount = new { currency_code = currency, value = Format(amount) }
                }
            }
        };

        var (_, body) = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", payload, idempotencyKey, preferRepresentation: true, ct);
        var order = JsonSerializer.Deserialize<OrderResponseDto>(body, JsonOptions);
        if (order?.Id is null)
        {
            throw new PayPalApiException(200, "ORDER_PARSE_ERROR", "PayPal did not return an order id.", null, Array.Empty<string>());
        }
        return new PayPalOrderResult(order.Id, order.Status ?? "CREATED");
    }

    public Task<PayPalAuthorizationResult> AuthorizeOrderWithCardAsync(
        string payPalOrderId, PayPalCardDetails card, string idempotencyKey, CancellationToken ct = default)
        => AuthorizeOrderAsync(payPalOrderId, new { card = BuildCardObject(card) }, idempotencyKey, ct);

    public Task<PayPalAuthorizationResult> AuthorizeOrderWithVaultAsync(
        string payPalOrderId, string vaultId, string idempotencyKey, CancellationToken ct = default)
        => AuthorizeOrderAsync(payPalOrderId, new { card = new { vault_id = vaultId } }, idempotencyKey, ct);

    private async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(
        string payPalOrderId, object paymentSource, string idempotencyKey, CancellationToken ct)
    {
        var payload = new { payment_source = paymentSource };
        var (_, body) = await SendAsync(
            HttpMethod.Post, $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize",
            payload, idempotencyKey, preferRepresentation: true, ct);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var orderStatus = root.TryGetProperty("status", out var st) ? st.GetString() : null;
        if (string.Equals(orderStatus, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) || HasPayerActionLink(root))
        {
            throw new PayerActionRequiredException(
                $"PayPal returned a payer-action challenge (e.g. 3-D Secure) for order {payPalOrderId}. " +
                "This card payment cannot be completed server-side without a browser approval step.");
        }

        var auth = FindFirstAuthorization(root)
            ?? throw new PayPalApiException(200, "AUTHORIZATION_MISSING",
                $"PayPal did not return an authorization for order {payPalOrderId} (order status {orderStatus}).", null, Array.Empty<string>());

        var (cardLast4, cardBrand) = ReadCardFromPaymentSource(root);
        return ParseAuthorization(auth, cardLast4, cardBrand);
    }

    private static bool HasPayerActionLink(JsonElement root)
    {
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            return links.EnumerateArray().Any(l =>
                l.TryGetProperty("rel", out var rel) &&
                string.Equals(rel.GetString(), "payer-action", StringComparison.OrdinalIgnoreCase));
        }
        return false;
    }

    private static JsonElement? FindFirstAuthorization(JsonElement root)
    {
        if (root.TryGetProperty("purchase_units", out var pus) && pus.ValueKind == JsonValueKind.Array)
        {
            foreach (var pu in pus.EnumerateArray())
            {
                if (pu.TryGetProperty("payments", out var payments)
                    && payments.TryGetProperty("authorizations", out var auths)
                    && auths.ValueKind == JsonValueKind.Array
                    && auths.GetArrayLength() > 0)
                {
                    return auths[0];
                }
            }
        }
        return null;
    }

    private static (string? last4, string? brand) ReadCardFromPaymentSource(JsonElement root)
    {
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var card))
        {
            var last4 = card.TryGetProperty("last_digits", out var l) ? l.GetString() : null;
            var brand = card.TryGetProperty("brand", out var b) ? b.GetString() : null;
            return (last4, brand);
        }
        return (null, null);
    }

    private PayPalAuthorizationResult ParseAuthorization(JsonElement auth, string? cardLast4, string? cardBrand)
    {
        var id = auth.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        var status = auth.TryGetProperty("status", out var stEl) ? stEl.GetString() : null;
        decimal amount = 0m;
        string currency = _settings.Currency;
        if (auth.TryGetProperty("amount", out var amtEl))
        {
            if (amtEl.TryGetProperty("value", out var v) && decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                amount = parsed;
            if (amtEl.TryGetProperty("currency_code", out var c)) currency = c.GetString() ?? currency;
        }
        DateTimeOffset? expires = null;
        if (auth.TryGetProperty("expiration_time", out var exp) && exp.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(exp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var e))
        {
            expires = e;
        }
        return new PayPalAuthorizationResult(id!, status ?? "CREATED", amount, currency, expires, cardLast4, cardBrand);
    }

    // --- Payments v2 ---

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        var (_, body) = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null, false, ct);
        using var doc = JsonDocument.Parse(body);
        return ParseAuthorization(doc.RootElement, null, null);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, decimal amount, string currency, string invoiceId, string idempotencyKey, CancellationToken ct = default)
    {
        var payload = new
        {
            amount = new { currency_code = currency, value = Format(amount) },
            invoice_id = invoiceId,
            final_capture = true
        };
        var (_, body) = await SendAsync(
            HttpMethod.Post, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            payload, idempotencyKey, preferRepresentation: true, ct);

        var capture = JsonSerializer.Deserialize<CaptureResponseDto>(body, JsonOptions)
            ?? throw new PayPalApiException(200, "CAPTURE_PARSE_ERROR", "Could not parse the PayPal capture response.", null, Array.Empty<string>());

        var breakdown = capture.SellerReceivableBreakdown;
        return new PayPalCaptureResult(
            capture.Id!,
            capture.Status ?? "COMPLETED",
            ParseMoney(breakdown?.GrossAmount) ?? ParseMoney(capture.Amount) ?? amount,
            ParseMoney(breakdown?.PayPalFee),
            ParseMoney(breakdown?.NetAmount),
            capture.Amount?.CurrencyCode ?? breakdown?.GrossAmount?.CurrencyCode ?? currency);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, CancellationToken ct = default)
    {
        var payload = new { amount = new { currency_code = currency, value = Format(amount) } };
        var (_, body) = await SendAsync(
            HttpMethod.Post, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            payload, null, preferRepresentation: true, ct);
        using var doc = JsonDocument.Parse(body);
        return ParseAuthorization(doc.RootElement, null, null);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void", null, null, false, ct);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        object? payload = amount is null
            ? new { } // full refund: no amount
            : new { amount = new { currency_code = currency, value = Format(amount.Value) } };

        var (_, body) = await SendAsync(
            HttpMethod.Post, $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            payload, idempotencyKey, preferRepresentation: true, ct);

        var refund = JsonSerializer.Deserialize<RefundResponseDto>(body, JsonOptions)
            ?? throw new PayPalApiException(200, "REFUND_PARSE_ERROR", "Could not parse the PayPal refund response.", null, Array.Empty<string>());

        return new PayPalRefundResult(
            refund.Id!,
            refund.Status ?? "COMPLETED",
            ParseMoney(refund.Amount) ?? amount ?? 0m,
            refund.Amount?.CurrencyCode ?? currency);
    }

    // --- Vault v3 ---

    public async Task<PayPalVaultCardResult> VaultCardAsync(
        PayPalCardDetails card, string customerId, string idempotencyKey, CancellationToken ct = default)
    {
        var payload = new
        {
            payment_source = new { card = BuildCardObject(card) },
            customer = new { id = customerId }
        };
        var (_, body) = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", payload, idempotencyKey, preferRepresentation: false, ct);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var vaultId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrEmpty(vaultId))
        {
            throw new PayPalApiException(200, "VAULT_PARSE_ERROR", "PayPal did not return a vault token id.", null, Array.Empty<string>());
        }
        var custId = customerId;
        if (root.TryGetProperty("customer", out var cust) && cust.TryGetProperty("id", out var cid))
        {
            custId = cid.GetString() ?? customerId;
        }

        string? brand = null, last4 = null, expiry = null, name = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var c))
        {
            brand = c.TryGetProperty("brand", out var b) ? b.GetString() : null;
            last4 = c.TryGetProperty("last_digits", out var l) ? l.GetString() : null;
            expiry = c.TryGetProperty("expiry", out var e) ? e.GetString() : null;
            name = c.TryGetProperty("name", out var n) ? n.GetString() : null;
        }
        return new PayPalVaultCardResult(vaultId!, custId, brand, last4, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}", null, null, false, ct);
    }

    // --- Transaction Search v1 ---

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<PayPalTransaction>();

        // PayPal's transaction search allows at most a 31-day window per request, so chunk the range.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to) windowEnd = to;

            await ReadAllPagesAsync(windowStart, windowEnd, results, ct);

            windowStart = windowEnd;
            if (windowEnd == to) break;
        }
        return results;
    }

    private async Task ReadAllPagesAsync(DateTimeOffset start, DateTimeOffset end, List<PayPalTransaction> results, CancellationToken ct)
    {
        int page = 1;
        int totalPages;
        do
        {
            var startStr = Uri.EscapeDataString(start.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
            var endStr = Uri.EscapeDataString(end.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
            var path = $"/v1/reporting/transactions?start_date={startStr}&end_date={endStr}&fields=transaction_info&page_size=100&page={page}";

            var (_, body) = await SendAsync(HttpMethod.Get, path, null, null, false, ct);
            var search = JsonSerializer.Deserialize<SearchResponseDto>(body, JsonOptions);
            if (search?.TransactionDetails is not null)
            {
                foreach (var td in search.TransactionDetails)
                {
                    var info = td.TransactionInfo;
                    if (info?.TransactionId is null) continue;
                    DateTimeOffset date = default;
                    if (info.TransactionInitiationDate is not null)
                        DateTimeOffset.TryParse(info.TransactionInitiationDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date);

                    results.Add(new PayPalTransaction(
                        info.TransactionId,
                        string.IsNullOrEmpty(info.InvoiceId) ? null : info.InvoiceId,
                        string.IsNullOrEmpty(info.CustomField) ? null : info.CustomField,
                        info.TransactionStatus ?? "",
                        info.TransactionEventCode,
                        ParseMoney(info.TransactionAmount) ?? 0m,
                        info.TransactionAmount?.CurrencyCode ?? _settings.Currency,
                        date));
                }
            }
            totalPages = search?.TotalPages ?? 1;
            page++;
        }
        while (page <= totalPages);
    }

    private static decimal? ParseMoney(MoneyDto? money)
    {
        if (money?.Value is null) return null;
        return decimal.TryParse(money.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}

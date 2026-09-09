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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal-backed <see cref="IPaymentGateway"/>. Talks to the PayPal REST API (Orders v2, Payments v2,
/// Vault v3, Transaction Search v1) over a typed <see cref="HttpClient"/>. All shapes and endpoints
/// follow the PayPal Payments API documentation.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // Currencies that ISO-4217 defines with no minor units, so amounts must be sent without decimals.
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "KRW", "VND", "HUF", "TWD", "CLP"
    };

    private readonly HttpClient _http;
    private readonly PayPalOptions _options;
    private readonly IAppLogger<PayPalPaymentGateway> _logger;

    // Access token cached process-wide: the merchant credentials are constant, and the typed HttpClient
    // is transient, so instance-level caching would re-authenticate on every request.
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? _accessToken;
    private static DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalPaymentGateway(HttpClient http, IOptions<PayPalOptions> options,
        IAppLogger<PayPalPaymentGateway> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public string Currency => _options.ResolvedCurrency;

    // ----------------------------------------------------------------- Authorize

    public async Task<AuthorizationResult> CreateAuthorizedOrderAsync(CreateAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        var card = BuildCardNode(request.Card, request.VaultTokenId);

        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["invoice_id"] = request.InvoiceId,
                    ["custom_id"] = request.InvoiceId,
                    ["description"] = request.Description,
                    ["amount"] = Amount(request.Amount)
                }
            },
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = card }
        };

        using var message = NewRequest(HttpMethod.Post, "/v2/checkout/orders", body);
        message.Headers.TryAddWithoutValidation("PayPal-Request-Id", request.RequestId);
        message.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        using var doc = await SendAsync(message, "create order", cancellationToken);
        var root = doc.RootElement;
        var payPalOrderId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;

        // Single-step card orders process on create; the authorization is under the purchase unit.
        if (TryGetAuthorization(root, out var auth))
            return new AuthorizationResult(payPalOrderId, auth.Id, auth.Status, auth.ExpiresAt);

        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentApprovalRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (e.g. a 3-D Secure " +
                "challenge). This integration does not implement a browser approval round-trip.");
        }

        // Card was validated but not yet authorized (status APPROVED/CREATED): create the authorization.
        using var authDoc = await AuthorizeOrderAsync(payPalOrderId, cancellationToken);
        if (TryGetAuthorization(authDoc.RootElement, out var auth2))
            return new AuthorizationResult(payPalOrderId, auth2.Id, auth2.Status, auth2.ExpiresAt);

        throw new PaymentGatewayException(
            $"PayPal order {payPalOrderId} did not yield an authorization (status {status}).");
    }

    private async Task<JsonDocument> AuthorizeOrderAsync(string payPalOrderId, CancellationToken cancellationToken)
    {
        using var message = NewRequest(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize",
            new Dictionary<string, object?>());
        message.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        return await SendAsync(message, "authorize order", cancellationToken);
    }

    public async Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        using var message = NewRequest(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null);
        using var doc = await SendAsync(message, "get authorization", cancellationToken);
        var root = doc.RootElement;
        return new AuthorizationDetails(
            root.GetProperty("id").GetString()!,
            root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "",
            ReadDate(root, "expiration_time"));
    }

    public async Task<AuthorizationDetails> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["amount"] = Amount(amount) };
        using var message = NewRequest(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", body);
        message.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        using var doc = await SendAsync(message, "reauthorize", cancellationToken);
        var root = doc.RootElement;
        return new AuthorizationDetails(
            root.GetProperty("id").GetString()!,
            root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "",
            ReadDate(root, "expiration_time"));
    }

    // ----------------------------------------------------------------- Capture / Void

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken = default)
    {
        using var message = NewRequest(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", new Dictionary<string, object?>
            {
                ["final_capture"] = true
            });
        message.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        message.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        using var doc = await SendAsync(message, "capture", cancellationToken);
        var root = doc.RootElement;

        var captureId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        var gross = ReadMoney(root, "amount") ?? 0m;
        decimal? fee = null;
        decimal? net = null;
        var currency = Currency;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            gross = ReadMoney(breakdown, "gross_amount") ?? gross;
            fee = ReadMoney(breakdown, "paypal_fee");
            net = ReadMoney(breakdown, "net_amount");
            currency = ReadCurrency(breakdown, "gross_amount") ?? currency;
        }

        return new CaptureResult(captureId, status, gross, fee, net, currency);
    }

    public async Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var message = NewRequest(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void", null);
        using var doc = await SendAsync(message, "void authorization", cancellationToken, allowEmpty: true);
    }

    // ----------------------------------------------------------------- Refund

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>();
        if (amount.HasValue)
            body["amount"] = Amount(amount.Value);

        using var message = NewRequest(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body);
        // Scope the caller's idempotency key to this (globally-unique) capture so the same key can be
        // reused legitimately against a different capture, and a genuine retry of THIS refund de-dupes.
        message.Headers.TryAddWithoutValidation("PayPal-Request-Id", $"{captureId}-{idempotencyKey}");
        message.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        using var doc = await SendAsync(message, "refund", cancellationToken);
        var root = doc.RootElement;
        var refundId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        var refundAmount = ReadMoney(root, "amount") ?? amount ?? 0m;
        var refundCurrency = ReadCurrency(root, "amount") ?? currency;
        return new RefundResult(refundId, status, refundAmount, refundCurrency);
    }

    // ----------------------------------------------------------------- Vault

    public async Task<VaultedCardResult> VaultCardAsync(CardDetails card,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = BuildVaultCardNode(card)
            }
        };

        using var message = NewRequest(HttpMethod.Post, "/v3/vault/payment-tokens", body);
        message.Headers.TryAddWithoutValidation("PayPal-Request-Id", Guid.NewGuid().ToString());

        using var doc = await SendAsync(message, "vault card", cancellationToken);
        var root = doc.RootElement;
        var tokenId = root.GetProperty("id").GetString()!;

        string? brand = null, last4 = null, expiry = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var c))
        {
            brand = c.TryGetProperty("brand", out var b) ? b.GetString() : null;
            last4 = c.TryGetProperty("last_digits", out var l) ? l.GetString() : null;
            expiry = c.TryGetProperty("expiry", out var e) ? e.GetString() : null;
        }

        return new VaultedCardResult(tokenId, brand, last4, expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        using var message = NewRequest(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultTokenId}", null);
        using var doc = await SendAsync(message, "delete vaulted card", cancellationToken, allowEmpty: true);
    }

    // ----------------------------------------------------------------- Reconciliation

    public async Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<ReconciliationTransaction>();

        // PayPal's transaction search accepts at most a 31-day window per query, so walk the whole
        // requested range in <=31-day windows and paginate each — the report covers the full range.
        var windowStart = from;
        while (windowStart < to && !cancellationToken.IsCancellationRequested)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to) windowEnd = to;

            await ReadAllPagesAsync(windowStart, windowEnd, results, cancellationToken);

            windowStart = windowEnd;
        }

        return results;
    }

    private async Task ReadAllPagesAsync(DateTimeOffset start, DateTimeOffset end,
        List<ReconciliationTransaction> results, CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var page = 1;
        var totalPages = 1;

        do
        {
            var query = $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(FormatDate(start))}" +
                        $"&end_date={Uri.EscapeDataString(FormatDate(end))}" +
                        $"&fields=all&page_size={pageSize}&page={page}";
            using var message = NewRequest(HttpMethod.Get, query, null);
            using var doc = await SendAsync(message, "transaction search", cancellationToken);
            var root = doc.RootElement;

            if (root.TryGetProperty("total_pages", out var tp) && tp.ValueKind == JsonValueKind.Number)
                totalPages = tp.GetInt32();

            if (root.TryGetProperty("transaction_details", out var details) &&
                details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (!detail.TryGetProperty("transaction_info", out var info))
                        continue;
                    results.Add(new ReconciliationTransaction(
                        info.TryGetProperty("transaction_id", out var id) ? id.GetString() ?? "" : "",
                        info.TryGetProperty("invoice_id", out var inv) ? inv.GetString() : null,
                        ReadMoney(info, "transaction_amount"),
                        ReadCurrency(info, "transaction_amount"),
                        info.TryGetProperty("transaction_status", out var st) ? st.GetString() : null,
                        ReadDate(info, "transaction_initiation_date"),
                        info.TryGetProperty("transaction_event_code", out var ev) ? ev.GetString() : null));
                }
            }

            page++;
        } while (page <= totalPages && !cancellationToken.IsCancellationRequested);
    }

    // ----------------------------------------------------------------- HTTP plumbing

    private HttpRequestMessage NewRequest(HttpMethod method, string relativeUrl, object? body)
    {
        var message = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }
        return message;
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage message, string operation,
        CancellationToken cancellationToken, bool allowEmpty = false)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentGatewayException($"PayPal {operation} request failed to reach the API: {ex.Message}", ex);
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw BuildError(operation, response.StatusCode, content);

        if (string.IsNullOrWhiteSpace(content))
        {
            if (allowEmpty)
                return JsonDocument.Parse("{}");
            throw new PaymentGatewayException($"PayPal {operation} returned an empty response.");
        }

        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException($"PayPal {operation} returned an unparseable response: {ex.Message}");
        }
    }

    private PaymentGatewayException BuildError(string operation, HttpStatusCode statusCode, string content)
    {
        string? debugId = null;
        string message = content;
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.TryGetProperty("debug_id", out var d)) debugId = d.GetString();
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            var desc = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            var detail = "";
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array &&
                details.GetArrayLength() > 0)
            {
                var first = details[0];
                var issue = first.TryGetProperty("issue", out var i) ? i.GetString() : null;
                var description = first.TryGetProperty("description", out var de) ? de.GetString() : null;
                detail = $" ({issue}: {description})";
            }
            message = $"{name}: {desc}{detail}";
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep raw content (truncated).
            if (message.Length > 500) message = message.Substring(0, 500);
        }

        _logger.LogWarning($"PayPal {operation} failed ({(int)statusCode}) debug_id={debugId}: {message}");
        return new PaymentGatewayException($"PayPal {operation} failed ({(int)statusCode}): {message}", debugId);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
            return _accessToken;

        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
                return _accessToken;

            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
                throw new PaymentGatewayException(
                    "PayPal credentials are not configured (PayPal:ClientId / PayPal:ClientSecret).");

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                })
            };
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.ParseAdd("application/json");

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new PaymentGatewayException($"Could not reach PayPal to obtain an access token: {ex.Message}", ex);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw BuildError("token request", response.StatusCode, content);

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            _accessToken = root.GetProperty("access_token").GetString();
            var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3000;
            // Refresh a minute early to avoid using a token that expires mid-flight.
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            return _accessToken!;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    // ----------------------------------------------------------------- JSON helpers

    private Dictionary<string, object?> Amount(decimal value) => new()
    {
        ["currency_code"] = Currency,
        ["value"] = FormatMoney(value)
    };

    private string FormatMoney(decimal value)
    {
        var decimals = ZeroDecimalCurrencies.Contains(Currency) ? 0 : 2;
        return Math.Round(value, decimals, MidpointRounding.AwayFromZero)
            .ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private Dictionary<string, object?> BuildCardNode(CardDetails? card, string? vaultTokenId)
    {
        if (!string.IsNullOrEmpty(vaultTokenId))
            return new Dictionary<string, object?> { ["vault_id"] = vaultTokenId };

        if (card is null)
            throw new PaymentGatewayException("A card or a saved-card token is required to authorize a payment.");

        var node = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["name"] = card.Name
        };
        var billing = BuildBillingAddress(card.BillingAddress, required: false);
        if (billing is not null)
            node["billing_address"] = billing;
        return node;
    }

    private Dictionary<string, object?> BuildVaultCardNode(CardDetails card)
    {
        return new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["name"] = card.Name,
            // Vaulting requires a billing address with at least a country code.
            ["billing_address"] = BuildBillingAddress(card.BillingAddress, required: true)
        };
    }

    private static Dictionary<string, object?>? BuildBillingAddress(CardBillingAddress? address, bool required)
    {
        if (address is null)
            return required ? new Dictionary<string, object?> { ["country_code"] = "US" } : null;

        return new Dictionary<string, object?>
        {
            ["address_line_1"] = address.AddressLine1,
            ["address_line_2"] = address.AddressLine2,
            ["admin_area_2"] = address.AdminArea2,
            ["admin_area_1"] = address.AdminArea1,
            ["postal_code"] = address.PostalCode,
            ["country_code"] = string.IsNullOrWhiteSpace(address.CountryCode) ? "US" : address.CountryCode
        };
    }

    private static bool TryGetAuthorization(JsonElement root, out (string Id, string Status, DateTimeOffset? ExpiresAt) auth)
    {
        auth = default;
        if (!root.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var unit in units.EnumerateArray())
        {
            if (!unit.TryGetProperty("payments", out var payments) ||
                !payments.TryGetProperty("authorizations", out var auths) ||
                auths.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var a in auths.EnumerateArray())
            {
                var id = a.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrEmpty(id)) continue;
                var status = a.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
                auth = (id, status, ReadDate(a, "expiration_time"));
                return true;
            }
        }
        return false;
    }

    private static decimal? ReadMoney(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var money) || money.ValueKind != JsonValueKind.Object)
            return null;
        if (!money.TryGetProperty("value", out var value)) return null;
        var raw = value.GetString();
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static string? ReadCurrency(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var money) || money.ValueKind != JsonValueKind.Object)
            return null;
        return money.TryGetProperty("currency_code", out var c) ? c.GetString() : null;
    }

    private static DateTimeOffset? ReadDate(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        return DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
    }
}

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

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The concrete PayPal gateway. Owns OAuth-token acquisition and caching, idempotency/representation
/// headers, JSON mapping, error classification, transaction-search paging, and honoring the optional
/// BaseUrl override for every call (including the token request). Card data flows straight to PayPal
/// and is never persisted or logged here.
/// </summary>
public class PayPalClient : IPayPalGateway
{
    // Transaction Search allows at most a 31-day window per request.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);
    private const int SearchPageSize = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public const string HttpClientName = "paypal";

    private readonly IHttpClientFactory _httpFactory;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalClient> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(IHttpClientFactory httpFactory, PayPalSettings settings, IAppLogger<PayPalClient> logger)
    {
        _httpFactory = httpFactory;
        _settings = settings;
        _logger = logger;
    }

    private string BaseUrl => _settings.ResolveBaseUrl();

    private HttpClient NewClient() => _httpFactory.CreateClient(HttpClientName);

    // ---------------------------------------------------------------- Authorize (hold funds)

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(
        decimal amount, string currency, string invoiceId, string customId,
        AuthorizeInstruction instruction, string requestId, CancellationToken ct = default)
    {
        var card = BuildCardPaymentSource(instruction);

        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["reference_id"] = "default",
                    ["invoice_id"] = invoiceId,
                    ["custom_id"] = customId,
                    ["amount"] = new { currency_code = currency, value = FormatAmount(amount) }
                }
            },
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = card }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId, ct);
        var root = doc.RootElement;

        var payPalOrderId = GetString(root, "id") ?? throw Unexpected("create order returned no id");
        var status = GetString(root, "status") ?? "UNKNOWN";

        var authorization = FindFirstAuthorization(root);
        if (authorization is null)
        {
            // No hold was created. If PayPal is asking the shopper to approve in a browser, surface it.
            if (RequiresPayerAction(root, status))
                throw new PayPalException(
                    "PayPal requires the shopper to approve this card payment in a browser (challenge / 3-D Secure). " +
                    "This integration does not build a browser approval round-trip.",
                    requiresBuyerApproval: true);

            throw Unexpected($"authorization was not created (order status {status})");
        }

        var auth = authorization.Value;
        var (brand, last4) = ReadCardMetadata(root);

        return new PayPalAuthorizationResult(
            payPalOrderId,
            GetString(auth, "id") ?? throw Unexpected("authorization has no id"),
            GetString(auth, "status") ?? "CREATED",
            ReadMoney(auth, "amount") ?? amount,
            currency,
            GetDate(auth, "expiration_time"),
            brand, last4);
    }

    // ---------------------------------------------------------------- Capture (take funds)

    public async Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId, decimal amount, string currency, string invoiceId,
        bool finalCapture, string requestId, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = new { currency_code = currency, value = FormatAmount(amount) },
            ["final_capture"] = finalCapture,
            ["invoice_id"] = invoiceId
        };

        JsonDocument doc;
        try
        {
            doc = await SendAsync(
                HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, requestId, ct);
        }
        catch (PayPalException ex) when (IsAuthorizationStale(ex))
        {
            // Re-raise flagged so the flow renews the hold instead of failing the fulfilment.
            throw new PayPalException(
                ex.Message, ex.StatusCode, ex.DebugId, ex.IssueName, authorizationNeedsRenewal: true);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var breakdown = TryGet(root, "seller_receivable_breakdown");
            var gross = (breakdown is not null ? ReadMoney(breakdown.Value, "gross_amount") : null) ?? amount;
            var fee = (breakdown is not null ? ReadMoney(breakdown.Value, "paypal_fee") : null) ?? 0m;
            var net = (breakdown is not null ? ReadMoney(breakdown.Value, "net_amount") : null) ?? (gross - fee);

            return new PayPalCaptureResult(
                GetString(root, "id") ?? throw Unexpected("capture has no id"),
                GetString(root, "status") ?? "COMPLETED",
                gross, fee, net, currency,
                GetBool(root, "final_capture") ?? finalCapture);
        }
    }

    // ---------------------------------------------------------------- Reauthorize (renew hold)

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, string requestId, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = new { currency_code = currency, value = FormatAmount(amount) }
        };

        using var doc = await SendAsync(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, requestId, ct);
        var root = doc.RootElement;

        return new PayPalAuthorizationResult(
            string.Empty,
            GetString(root, "id") ?? throw Unexpected("reauthorization has no id"),
            GetString(root, "status") ?? "CREATED",
            ReadMoney(root, "amount") ?? amount,
            currency,
            GetDate(root, "expiration_time"),
            null, null);
    }

    // ---------------------------------------------------------------- Void (release hold)

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken ct = default)
    {
        using var _ = await SendAsync(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", body: null, requestId, ct,
            allowEmptyResponse: true);
    }

    // ---------------------------------------------------------------- Refund

    public async Task<PayPalRefundResult> RefundAsync(
        string captureId, decimal? amount, string currency, string invoiceId,
        string requestId, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["invoice_id"] = invoiceId };
        if (amount.HasValue)
            body["amount"] = new { currency_code = currency, value = FormatAmount(amount.Value) };

        using var doc = await SendAsync(
            HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, requestId, ct);
        var root = doc.RootElement;

        return new PayPalRefundResult(
            GetString(root, "id") ?? throw Unexpected("refund has no id"),
            GetString(root, "status") ?? "COMPLETED",
            ReadMoney(root, "amount") ?? amount ?? 0m,
            currency);
    }

    // ---------------------------------------------------------------- Vault a card

    public async Task<PayPalVaultCardResult> VaultCardAsync(
        CardDetails card, string? customerId, string requestId, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = BuildRawCard(card) }
        };
        if (!string.IsNullOrEmpty(customerId))
            body["customer"] = new { id = customerId };

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, requestId, ct);
        var root = doc.RootElement;

        var vaultId = GetString(root, "id") ?? throw Unexpected("vault token has no id");
        var returnedCustomer = TryGet(root, "customer") is { } c ? GetString(c, "id") : null;

        string? brand = null, last4 = null, expiry = null, name = null;
        if (TryGet(root, "payment_source") is { } ps && TryGet(ps, "card") is { } cardEl)
        {
            brand = GetString(cardEl, "brand");
            last4 = GetString(cardEl, "last_digits");
            expiry = GetString(cardEl, "expiry");
            name = GetString(cardEl, "name");
        }

        return new PayPalVaultCardResult(vaultId, returnedCustomer ?? customerId ?? vaultId, brand, last4, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/v3/vault/payment-tokens/{vaultId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await NewClient().SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning($"Vault token {vaultId} was already absent when deleting; treating as removed.");
            return;
        }
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(ct);
            ThrowFromError((int)response.StatusCode, content);
        }
    }

    // ---------------------------------------------------------------- Transaction search / reconciliation

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (to < from)
            (from, to) = (to, from);

        var results = new List<PayPalTransaction>();

        // Cover the whole range by chunking into <=31-day windows, paging each window fully.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxSearchWindow;
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            int totalPages;
            do
            {
                var query =
                    $"?start_date={Rfc3339(windowStart)}&end_date={Rfc3339(windowEnd)}" +
                    $"&fields=transaction_info&balance_affecting_records_only=N" +
                    $"&page_size={SearchPageSize}&page={page}";

                using var doc = await SendAsync(HttpMethod.Get, $"/v1/reporting/transactions{query}", null, null, ct);
                var root = doc.RootElement;

                if (TryGet(root, "transaction_details") is { } details && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        var parsed = ParseTransaction(detail);
                        if (parsed is not null) results.Add(parsed);
                    }
                }

                totalPages = GetInt(root, "total_pages") ?? 1;
                page++;
            }
            while (page <= totalPages);

            // Advance past this window (guard against a zero-length final window).
            windowStart = windowEnd == windowStart ? to : windowEnd;
        }

        return results;
    }

    private static PayPalTransaction? ParseTransaction(JsonElement detail)
    {
        if (TryGet(detail, "transaction_info") is not { } info)
            return null;

        var id = GetString(info, "transaction_id") ?? string.Empty;
        var amount = ReadMoney(info, "transaction_amount") ?? 0m;
        var currency = ReadCurrency(info, "transaction_amount") ?? string.Empty;
        var fee = ReadMoney(info, "fee_amount");
        var status = GetString(info, "transaction_status") ?? string.Empty;
        var eventCode = GetString(info, "transaction_event_code") ?? string.Empty;
        var invoiceId = GetString(info, "invoice_id");
        var customField = GetString(info, "custom_field");
        var date = GetDate(info, "transaction_initiation_date") ?? GetDate(info, "transaction_updated_date") ?? default;

        return new PayPalTransaction(id, invoiceId, customField, amount, currency, fee, status, eventCode, date);
    }

    // ---------------------------------------------------------------- HTTP / auth plumbing

    private async Task<JsonDocument> SendAsync(
        HttpMethod method, string path, object? body, string? requestId, CancellationToken ct,
        bool allowEmptyResponse = false)
    {
        var response = await SendOnceAsync(method, path, body, requestId, ct);

        // A stale token yields 401; refresh once and retry.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            InvalidateToken();
            response = await SendOnceAsync(method, path, body, requestId, ct);
        }

        var content = await response.Content.ReadAsStringAsync(ct);
        var statusCode = (int)response.StatusCode;
        response.Dispose();

        if (statusCode is < 200 or >= 300)
            ThrowFromError(statusCode, content);

        if (string.IsNullOrWhiteSpace(content))
        {
            if (allowEmptyResponse)
                return JsonDocument.Parse("{}");
            return JsonDocument.Parse("{}");
        }

        return JsonDocument.Parse(content);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method, string path, object? body, string? requestId, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);
        using var request = new HttpRequestMessage(method, $"{BaseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // Full resource representation so we get authorization/capture/refund details back.
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(requestId))
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await NewClient().SendAsync(request, ct);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            return _accessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
                return _accessToken;

            _settings.Validate();

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/oauth2/token");
            var basic = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await NewClient().SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                ThrowFromError((int)response.StatusCode, content, "Failed to obtain a PayPal access token");

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var token = GetString(root, "access_token")
                ?? throw new PayPalException("PayPal token response contained no access_token.");
            var expiresIn = GetInt(root, "expires_in") ?? 3000;

            _accessToken = token;
            // Refresh a minute early to avoid using a token that expires mid-flight.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            _logger.LogInformation($"Obtained PayPal access token (expires in {expiresIn}s).");
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void InvalidateToken()
    {
        _accessToken = null;
        _tokenExpiresAt = DateTimeOffset.MinValue;
    }

    // ---------------------------------------------------------------- request-body builders

    private static object BuildCardPaymentSource(AuthorizeInstruction instruction)
    {
        if (instruction.UsesSavedCard)
            return new Dictionary<string, object?> { ["vault_id"] = instruction.VaultId };

        if (instruction.Card is null)
            throw new PayPalException("No card or saved card was supplied for the authorization.");

        return BuildRawCard(instruction.Card);
    }

    private static Dictionary<string, object?> BuildRawCard(CardDetails card)
    {
        var result = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = NormalizeExpiry(card.Expiry)
        };
        if (!string.IsNullOrWhiteSpace(card.SecurityCode)) result["security_code"] = card.SecurityCode;
        if (!string.IsNullOrWhiteSpace(card.Name)) result["name"] = card.Name;

        if (card.BillingAddress is { } addr)
        {
            var billing = new Dictionary<string, object?> { ["country_code"] = addr.CountryCode };
            if (!string.IsNullOrWhiteSpace(addr.AddressLine1)) billing["address_line_1"] = addr.AddressLine1;
            if (!string.IsNullOrWhiteSpace(addr.AddressLine2)) billing["address_line_2"] = addr.AddressLine2;
            if (!string.IsNullOrWhiteSpace(addr.AdminArea1)) billing["admin_area_1"] = addr.AdminArea1;
            if (!string.IsNullOrWhiteSpace(addr.AdminArea2)) billing["admin_area_2"] = addr.AdminArea2;
            if (!string.IsNullOrWhiteSpace(addr.PostalCode)) billing["postal_code"] = addr.PostalCode;
            result["billing_address"] = billing;
        }

        return result;
    }

    // ---------------------------------------------------------------- JSON / error helpers

    private static JsonElement? FindFirstAuthorization(JsonElement orderRoot)
    {
        if (TryGet(orderRoot, "purchase_units") is not { } units || units.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var unit in units.EnumerateArray())
        {
            if (TryGet(unit, "payments") is { } payments &&
                TryGet(payments, "authorizations") is { } auths &&
                auths.ValueKind == JsonValueKind.Array)
            {
                foreach (var auth in auths.EnumerateArray())
                    return auth;
            }
        }
        return null;
    }

    private static (string? Brand, string? Last4) ReadCardMetadata(JsonElement orderRoot)
    {
        if (TryGet(orderRoot, "payment_source") is { } ps && TryGet(ps, "card") is { } card)
            return (GetString(card, "brand"), GetString(card, "last_digits"));
        return (null, null);
    }

    private static bool RequiresPayerAction(JsonElement root, string status)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            return true;
        if (TryGet(root, "links") is { } links && links.ValueKind == JsonValueKind.Array)
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

    private static bool IsAuthorizationStale(PayPalException ex) =>
        string.Equals(ex.IssueName, "AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase);

    private void ThrowFromError(int statusCode, string content, string? prefix = null)
    {
        string? name = null, message = null, debugId = null, issue = null;
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            name = GetString(root, "name");
            message = GetString(root, "message");
            debugId = GetString(root, "debug_id");
            if (TryGet(root, "details") is { } details && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in details.EnumerateArray())
                {
                    issue = GetString(d, "issue");
                    if (issue is not null) break;
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with what we have.
        }

        var issueName = issue ?? name;
        var text = $"{prefix ?? "PayPal request failed"} ({statusCode})" +
                   (issueName is not null ? $" [{issueName}]" : string.Empty) +
                   (message is not null ? $": {message}" : string.Empty) +
                   (debugId is not null ? $" (debug_id: {debugId})" : string.Empty);

        _logger.LogWarning(text);
        throw new PayPalException(text, statusCode, debugId, issueName);
    }

    private PayPalException Unexpected(string what) =>
        new($"Unexpected PayPal response: {what}.");

    private static JsonElement? TryGet(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : null;

    private static string? GetString(JsonElement element, string name) =>
        TryGet(element, name) is { } v && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement element, string name)
    {
        var v = TryGet(element, name);
        if (v is null) return null;
        if (v.Value.ValueKind == JsonValueKind.Number && v.Value.TryGetInt32(out var i)) return i;
        if (v.Value.ValueKind == JsonValueKind.String && int.TryParse(v.Value.GetString(), out var s)) return s;
        return null;
    }

    private static bool? GetBool(JsonElement element, string name) =>
        TryGet(element, name) is { } v && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean() : null;

    private static DateTimeOffset? GetDate(JsonElement element, string name)
    {
        var s = GetString(element, name);
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
            ? d : null;
    }

    private static decimal? ReadMoney(JsonElement parent, string moneyProperty)
    {
        if (TryGet(parent, moneyProperty) is not { } money) return null;
        var value = GetString(money, "value");
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static string? ReadCurrency(JsonElement parent, string moneyProperty) =>
        TryGet(parent, moneyProperty) is { } money ? GetString(money, "currency_code") : null;

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Rfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>Normalizes common expiry inputs to PayPal's <c>YYYY-MM</c>.</summary>
    private static string NormalizeExpiry(string expiry)
    {
        var trimmed = (expiry ?? string.Empty).Trim();
        // Already YYYY-MM
        if (trimmed.Length == 7 && trimmed[4] == '-') return trimmed;

        var parts = trimmed.Split('/', '-');
        if (parts.Length == 2)
        {
            var a = parts[0];
            var b = parts[1];
            // YYYY-MM style split by '-' would have been caught above; handle MM/YYYY and MM/YY.
            if (a.Length == 4) return $"{a}-{b.PadLeft(2, '0')}";        // YYYY/MM
            var month = a.PadLeft(2, '0');
            var year = b.Length == 2 ? $"20{b}" : b.PadLeft(4, '0');      // MM/YY or MM/YYYY
            return $"{year}-{month}";
        }
        return trimmed;
    }
}

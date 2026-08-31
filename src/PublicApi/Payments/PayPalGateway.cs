using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalGateway : IPayPalGateway
{
    private readonly HttpClient _http;
    private readonly PayPalOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PayPalGateway(HttpClient http, IOptions<PayPalOptions> options)
    { _http = http; _options = options.Value; }

    public string Currency => _options.Currency.ToUpperInvariant();
    private string BaseUrl => !string.IsNullOrWhiteSpace(_options.BaseUrl)
        ? _options.BaseUrl.TrimEnd('/')
        : _options.Environment.Equals("Live", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";

    public async Task<PayPalAuthorization> AuthorizeAsync(string paymentReference, decimal amount, CardDto? card, string? vaultId, CancellationToken ct)
    {
        var value = Format(amount);
        using var created = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", new
        {
            intent = "AUTHORIZE",
            purchase_units = new[] { new { reference_id = paymentReference, invoice_id = $"eshop-order-{paymentReference}", custom_id = paymentReference, amount = new { currency_code = Currency, value } } }
        }, $"order-create-{paymentReference}", ct);
        var paypalOrderId = RequiredString(created.RootElement, "id");

        object source = vaultId != null
            ? new { card = new { vault_id = vaultId } }
            : new { card = new { number = card!.Number, expiry = card.Expiry, security_code = card.SecurityCode, name = card.Name, billing_address = Address(card.BillingAddress) } };
        using var authorized = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{Uri.EscapeDataString(paypalOrderId)}/authorize",
            new { payment_source = source }, $"order-authorize-{paymentReference}", ct);
        if (String(authorized.RootElement, "status") == "PAYER_ACTION_REQUIRED") throw new PaymentActionRequiredException();
        var auth = authorized.RootElement.GetProperty("purchase_units")[0].GetProperty("payments").GetProperty("authorizations")[0];
        return ParseAuthorization(paypalOrderId, auth);
    }

    public async Task<PayPalAuthorization> ReauthorizeAsync(string paymentReference, string authorizationId, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize", new { }, $"reauthorize-{paymentReference}", ct);
        return ParseAuthorization(string.Empty, response.RootElement);
    }

    public async Task<PayPalCapture> CaptureAsync(string paymentReference, string authorizationId, decimal amount, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new { amount = new { currency_code = Currency, value = Format(amount) }, final_capture = true }, $"capture-{paymentReference}", ct);
        var root = response.RootElement;
        var breakdown = root.TryGetProperty("seller_receivable_breakdown", out var b) ? b : default;
        return new PayPalCapture(RequiredString(root, "id"), RequiredString(root, "status"), Money(root, "amount"), CurrencyOf(root, "amount"),
            NullableMoney(breakdown, "paypal_fee"), NullableMoney(breakdown, "net_amount"), Date(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task<string> VoidAsync(string paymentReference, string authorizationId, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void", new { }, $"void-{paymentReference}", ct);
        return response.RootElement.ValueKind == JsonValueKind.Object ? String(response.RootElement, "status") ?? "VOIDED" : "VOIDED";
    }

    public async Task<PayPalRefund> RefundAsync(string paymentReference, string captureId, decimal amount, string key, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new { amount = new { currency_code = Currency, value = Format(amount) } }, $"refund-{paymentReference}-{Hash(key)}", ct);
        var root = response.RootElement;
        return new PayPalRefund(RequiredString(root, "id"), RequiredString(root, "status"), Money(root, "amount"), CurrencyOf(root, "amount"), Date(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task<PayPalSavedCard> SaveCardAsync(string shopperId, string? customerId, CardDto card, CancellationToken ct)
    {
        object customer = customerId == null ? new { merchant_customer_id = Hash(shopperId) } : new { id = customerId };
        using var setup = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", new
        {
            customer,
            payment_source = new { card = new { number = card.Number, expiry = card.Expiry, security_code = card.SecurityCode, name = card.Name, billing_address = Address(card.BillingAddress) } }
        }, $"vault-setup-{Guid.NewGuid():N}", ct);
        if (String(setup.RootElement, "status") == "PAYER_ACTION_REQUIRED") throw new PaymentActionRequiredException();
        var setupId = RequiredString(setup.RootElement, "id");
        using var token = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", new { payment_source = new { token = new { id = setupId, type = "SETUP_TOKEN" } } }, $"vault-token-{setupId}", ct);
        var root = token.RootElement;
        var source = root.GetProperty("payment_source").GetProperty("card");
        var paypalCustomerId = root.TryGetProperty("customer", out var c) ? RequiredString(c, "id") : customerId ?? throw new InvalidOperationException("PayPal did not return a customer ID.");
        return new PayPalSavedCard(RequiredString(root, "id"), paypalCustomerId, RequiredString(source, "brand"), RequiredString(source, "last_digits"), RequiredString(source, "expiry"), String(source, "name"));
    }

    public async Task DeletePaymentTokenAsync(string tokenId, CancellationToken ct)
    { using var ignored = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{Uri.EscapeDataString(tokenId)}", null, null, ct); }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var all = new Dictionary<string, PayPalTransaction>(StringComparer.Ordinal);
        var cursor = from.ToUniversalTime(); var final = to.ToUniversalTime();
        while (cursor < final)
        {
            var windowEnd = cursor.AddDays(31); if (windowEnd > final) windowEnd = final;
            var page = 1; var totalPages = 1;
            do
            {
                var path = $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(Iso(cursor))}&end_date={Uri.EscapeDataString(Iso(windowEnd))}&fields=transaction_info&balance_affecting_records_only=N&page_size=500&page={page}";
                using var response = await SendAsync(HttpMethod.Get, path, null, null, ct, true);
                var root = response.RootElement;
                totalPages = root.TryGetProperty("total_pages", out var tp) ? Math.Max(1, tp.GetInt32()) : 1;
                if (root.TryGetProperty("transaction_details", out var details)) foreach (var detail in details.EnumerateArray())
                {
                    var info = detail.GetProperty("transaction_info"); var tx = ParseTransaction(info);
                    all[$"{tx.TransactionId}|{tx.EventCode}|{tx.InitiatedAt:O}"] = tx;
                }
                page++;
            } while (page <= totalPages);
            cursor = windowEnd;
        }
        return all.Values.OrderBy(x => x.InitiatedAt).ToList();
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, string? requestId, CancellationToken ct, bool isoHeader = false)
    {
        EnsureConfigured();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(method, BaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(ct));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (requestId != null) request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId.Length <= 108 ? requestId : Hash(requestId));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (isoHeader) request.Headers.TryAddWithoutValidation("PayPal-Enforce-ISO8601-Format", "true");
            if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) { _accessToken = null; continue; }
            var content = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) throw ParseError((int)response.StatusCode, content);
            return string.IsNullOrWhiteSpace(content) ? JsonDocument.Parse("{}") : JsonDocument.Parse(content);
        }
        throw new InvalidOperationException("PayPal authentication retry failed.");
    }

    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken != null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_accessToken != null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;
            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/v1/oauth2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
            using var response = await _http.SendAsync(request, ct); var content = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) throw ParseError((int)response.StatusCode, content);
            using var json = JsonDocument.Parse(content); _accessToken = RequiredString(json.RootElement, "access_token");
            var seconds = json.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds); return _accessToken;
        }
        finally { _tokenLock.Release(); }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret)) throw new InvalidOperationException("PayPal credentials are not configured in PayPal:ClientId and PayPal:ClientSecret.");
        if (Currency.Length != 3) throw new InvalidOperationException("PayPal:Currency must be a three-letter currency code.");
    }
    private static object Address(CardAddressDto a) => new { address_line_1 = a.AddressLine1, address_line_2 = a.AddressLine2, admin_area_2 = a.AdminArea2, admin_area_1 = a.AdminArea1, postal_code = a.PostalCode, country_code = a.CountryCode.ToUpperInvariant() };
    private static PayPalAuthorization ParseAuthorization(string orderId, JsonElement a) => new(orderId, RequiredString(a, "id"), RequiredString(a, "status"), Money(a, "amount"), CurrencyOf(a, "amount"), Date(a, "create_time") ?? DateTimeOffset.UtcNow, Date(a, "expiration_time") ?? DateTimeOffset.UtcNow.AddDays(29));
    private static PayPalTransaction ParseTransaction(JsonElement i) => new(RequiredString(i, "transaction_id"), String(i, "paypal_reference_id"), String(i, "paypal_reference_id_type"), String(i, "transaction_event_code"), Date(i, "transaction_initiation_date") ?? DateTimeOffset.MinValue, Date(i, "transaction_updated_date"), NullableMoney(i, "transaction_amount"), CurrencyOfNullable(i, "transaction_amount"), NullableMoney(i, "fee_amount"), String(i, "transaction_status"), String(i, "invoice_id"));
    private static PayPalException ParseError(int code, string json) { try { using var d = JsonDocument.Parse(json); var r = d.RootElement; string? issue = null; if (r.TryGetProperty("details", out var details) && details.GetArrayLength() > 0) issue = String(details[0], "issue"); return new PayPalException(code, String(r, "name") ?? "ERROR", String(r, "message") ?? "Request failed.", issue, String(r, "debug_id")); } catch (JsonException) { return new PayPalException(code, "HTTP_ERROR", "The PayPal request failed.", null, null); } }
    private static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Iso(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string RequiredString(JsonElement e, string name) => String(e, name) ?? throw new InvalidOperationException($"PayPal response omitted {name}.");
    private static string? String(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static DateTimeOffset? Date(JsonElement e, string name) => DateTimeOffset.TryParse(String(e, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;
    private static decimal Money(JsonElement e, string name) => NullableMoney(e, name) ?? throw new InvalidOperationException($"PayPal response omitted {name}.value.");
    private static decimal? NullableMoney(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var m) && decimal.TryParse(String(m, "value"), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    private static string CurrencyOf(JsonElement e, string name) => CurrencyOfNullable(e, name) ?? throw new InvalidOperationException($"PayPal response omitted {name}.currency_code.");
    private static string? CurrencyOfNullable(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var m) ? String(m, "currency_code") : null;
}

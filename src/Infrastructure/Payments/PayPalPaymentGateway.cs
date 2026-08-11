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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal REST implementation of <see cref="IPayPalPaymentGateway"/>. Owns OAuth token caching,
/// idempotency headers, JSON (de)serialization and error mapping. No full card details are ever
/// logged; only PayPal-owned identifiers appear in logs.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private const string JsonMediaType = "application/json";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

    // Transaction Search is limited to ~31-day windows per request.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);
    private const int SearchPageSize = 500;

    public PayPalPaymentGateway(IHttpClientFactory httpClientFactory,
        IOptions<PayPalSettings> settings,
        ILogger<PayPalPaymentGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public string Currency => _settings.Currency;

    // ------------------------------------------------------------------ Authorize

    public async Task<AuthorizationResult> AuthorizeOrderAsync(string orderReference, decimal amount,
        string currency, PaymentInstrument instrument, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["reference_id"] = orderReference,
                    // custom_id flows into Transaction Search as custom_field, our reconciliation key.
                    ["custom_id"] = orderReference,
                    ["amount"] = Money(amount, currency)
                }
            },
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = BuildCardSource(instrument)
            }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body,
            idempotencyKey: idempotencyKey, preferRepresentation: true, cancellationToken: cancellationToken);

        var root = doc!.RootElement;
        var payPalOrderId = root.GetProperty("id").GetString()!;

        var authorization = TryGetFirstAuthorization(root);
        if (authorization is null)
        {
            // No hold was placed — most commonly the card needs shopper approval (a challenge).
            var status = root.TryGetProperty("status", out var st) ? st.GetString() : null;
            throw new PayPalGatewayException(
                $"PayPal did not authorize the payment (order status '{status}'). No authorization was returned; " +
                "the card may require additional shopper approval, which this integration does not support.",
                payPalErrorName: "AUTHORIZATION_NOT_CREATED");
        }

        var auth = authorization.Value;
        return new AuthorizationResult(
            payPalOrderId,
            auth.GetProperty("id").GetString()!,
            auth.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "",
            ReadDateTime(auth, "expiration_time"));
    }

    public async Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null,
            cancellationToken: cancellationToken);
        var root = doc!.RootElement;
        return new AuthorizationResult(
            string.Empty,
            root.GetProperty("id").GetString()!,
            root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "",
            ReadDateTime(root, "expiration_time"));
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["amount"] = Money(amount, currency) };
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body, preferRepresentation: true, cancellationToken: cancellationToken);
        var root = doc!.RootElement;
        return new AuthorizationResult(
            string.Empty,
            root.GetProperty("id").GetString()!,
            root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "",
            ReadDateTime(root, "expiration_time"));
    }

    // ------------------------------------------------------------------ Capture

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["final_capture"] = true };
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture",
            body, idempotencyKey: idempotencyKey, preferRepresentation: true, cancellationToken: cancellationToken);

        var root = doc!.RootElement;
        var captureId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        var (grossValue, grossCurrency) = ReadMoney(root, "amount") ?? (0m, currency: _settings.Currency);

        decimal? fee = null, net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = ReadMoney(breakdown, "paypal_fee")?.value;
            net = ReadMoney(breakdown, "net_amount")?.value;
            var gross = ReadMoney(breakdown, "gross_amount");
            if (gross is not null) { grossValue = gross.Value.value; grossCurrency = gross.Value.currency; }
        }

        return new CaptureResult(captureId, status, grossValue, fee, net, grossCurrency);
    }

    // ------------------------------------------------------------------ Void

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void",
            null, cancellationToken: cancellationToken);
    }

    // ------------------------------------------------------------------ Refund

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object? body = amount.HasValue
            ? new Dictionary<string, object?> { ["amount"] = Money(amount.Value, currency) }
            : new Dictionary<string, object?>(); // empty body = full refund

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund",
            body, idempotencyKey: idempotencyKey, preferRepresentation: true, cancellationToken: cancellationToken);

        var root = doc!.RootElement;
        var refundId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        var money = ReadMoney(root, "amount");
        return new RefundResult(refundId, status,
            money?.value ?? amount ?? 0m,
            money?.currency ?? currency);
    }

    // ------------------------------------------------------------------ Vault (save card)

    public async Task<VaultResult> VaultCardAsync(CardDetails card, string customerReference,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["customer"] = new Dictionary<string, object?> { ["merchant_customer_id"] = customerReference },
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = BuildRawCard(card) }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body,
            idempotencyKey: Guid.NewGuid().ToString("N"), cancellationToken: cancellationToken);

        var root = doc!.RootElement;
        var vaultId = root.GetProperty("id").GetString()!;
        var customerId = root.TryGetProperty("customer", out var cust) && cust.TryGetProperty("id", out var cid)
            ? cid.GetString() ?? string.Empty
            : string.Empty;

        string? brand = null, last4 = null, expiry = null, name = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var c))
        {
            brand = GetStringOrNull(c, "brand");
            last4 = GetStringOrNull(c, "last_digits");
            expiry = GetStringOrNull(c, "expiry");
            name = GetStringOrNull(c, "name");
        }

        return new VaultResult(vaultId, customerId, brand, last4, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var _ = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}",
                null, cancellationToken: cancellationToken);
        }
        catch (PayPalGatewayException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            // Already gone at PayPal — deletion is idempotent from the app's perspective.
            _logger.LogInformation("Vault token {VaultId} was already absent at PayPal on delete.", vaultId);
        }
    }

    // ------------------------------------------------------------------ Transaction Search (reconciliation)

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();
        if (to < from) (from, to) = (to, from);

        var windowStart = from.ToUniversalTime();
        var end = to.ToUniversalTime();

        // Chunk the range into <=31-day windows, then fully paginate each window.
        while (windowStart < end)
        {
            var windowEnd = windowStart + MaxSearchWindow;
            if (windowEnd > end) windowEnd = end;

            var page = 1;
            int totalPages;
            do
            {
                var url = "/v1/reporting/transactions" +
                          $"?start_date={Uri.EscapeDataString(FormatRfc3339(windowStart))}" +
                          $"&end_date={Uri.EscapeDataString(FormatRfc3339(windowEnd))}" +
                          "&fields=transaction_info" +
                          $"&page_size={SearchPageSize}&page={page}";

                using var doc = await SendAsync(HttpMethod.Get, url, null, cancellationToken: cancellationToken);
                var root = doc!.RootElement;

                totalPages = root.TryGetProperty("total_pages", out var tp) && tp.ValueKind == JsonValueKind.Number
                    ? tp.GetInt32() : 1;

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d in details.EnumerateArray())
                    {
                        if (!d.TryGetProperty("transaction_info", out var info)) continue;
                        var money = ReadMoney(info, "transaction_amount");
                        var fee = ReadMoney(info, "fee_amount");
                        results.Add(new PayPalTransaction(
                            GetStringOrNull(info, "transaction_id") ?? string.Empty,
                            GetStringOrNull(info, "transaction_status"),
                            money?.value,
                            money?.currency,
                            fee?.value,
                            GetStringOrNull(info, "invoice_id"),
                            GetStringOrNull(info, "custom_field"),
                            ReadDateTime(info, "transaction_initiation_date")));
                    }
                }

                page++;
            }
            while (page <= totalPages);

            // Advance one second past the window end to avoid re-fetching the boundary transaction.
            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    // ------------------------------------------------------------------ HTTP plumbing

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string pathAndQuery, object? body,
        string? idempotencyKey = null, bool preferRepresentation = false, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        var token = await GetAccessTokenAsync(client, cancellationToken);

        using var request = new HttpRequestMessage(method, pathAndQuery);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonMediaType));
        if (!string.IsNullOrEmpty(idempotencyKey))
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        if (preferRepresentation)
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body);
            request.Content = new StringContent(json, Encoding.UTF8, JsonMediaType);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw BuildError(response.StatusCode, content, $"{method} {pathAndQuery}");

        _logger.LogInformation("PayPal {Method} {Path} -> {Status}", method, pathAndQuery, (int)response.StatusCode);

        if (string.IsNullOrWhiteSpace(content))
            return null;
        return JsonDocument.Parse(content);
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("PayPal");
        if (client.BaseAddress is null)
            client.BaseAddress = new Uri(_settings.ResolveBaseUrl());
        return client;
    }

    private async Task<string> GetAccessTokenAsync(HttpClient client, CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
            return _accessToken;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
                return _accessToken;

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonMediaType));
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await client.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw BuildError(response.StatusCode, content, "POST /v1/oauth2/token (authenticate)");

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var token = root.GetProperty("access_token").GetString()!;
            var expiresIn = root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number
                ? e.GetInt32() : 300;

            _accessToken = token;
            // Refresh a minute early to avoid using a token that expires mid-flight.
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private PayPalGatewayException BuildError(HttpStatusCode statusCode, string content, string operation)
    {
        string? name = null, message = null, debugId = null;
        var details = new StringBuilder();
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            name = GetStringOrNull(root, "name");
            message = GetStringOrNull(root, "message");
            debugId = GetStringOrNull(root, "debug_id");
            if (root.TryGetProperty("details", out var det) && det.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in det.EnumerateArray())
                {
                    var issue = GetStringOrNull(d, "issue");
                    var desc = GetStringOrNull(d, "description");
                    if (issue is not null || desc is not null)
                        details.Append($" [{issue}: {desc}]");
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with raw content trimmed.
        }

        var summary = message ?? (content.Length > 0 ? content[..Math.Min(content.Length, 300)] : "no body");
        var full = $"PayPal request failed ({(int)statusCode} on {operation}): {name}: {summary}{details}" +
                   (debugId is not null ? $" (debug_id={debugId})" : string.Empty);

        _logger.LogWarning("PayPal error on {Operation}: {Status} {Name} {DebugId}", operation, (int)statusCode, name, debugId);
        return new PayPalGatewayException(full, (int)statusCode, name, debugId);
    }

    // ------------------------------------------------------------------ JSON helpers

    private object BuildCardSource(PaymentInstrument instrument)
    {
        if (instrument.IsVaulted)
            return new Dictionary<string, object?> { ["vault_id"] = instrument.VaultId };
        return BuildRawCard(instrument.Card!);
    }

    private static Dictionary<string, object?> BuildRawCard(CardDetails card)
    {
        var billing = new Dictionary<string, object?>
        {
            ["country_code"] = card.BillingAddress.CountryCode
        };
        AddIfPresent(billing, "address_line_1", card.BillingAddress.AddressLine1);
        AddIfPresent(billing, "address_line_2", card.BillingAddress.AddressLine2);
        AddIfPresent(billing, "admin_area_2", card.BillingAddress.AdminArea2);
        AddIfPresent(billing, "admin_area_1", card.BillingAddress.AdminArea1);
        AddIfPresent(billing, "postal_code", card.BillingAddress.PostalCode);

        return new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["name"] = card.CardholderName,
            ["billing_address"] = billing
        };
    }

    private static void AddIfPresent(Dictionary<string, object?> dict, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) dict[key] = value;
    }

    private Dictionary<string, object?> Money(decimal amount, string currency) => new()
    {
        ["currency_code"] = currency,
        ["value"] = FormatAmount(amount, currency)
    };

    private static string FormatAmount(decimal amount, string currency)
    {
        // Currencies without a minor unit take no decimals; everything else here uses two.
        var zeroDecimal = currency.ToUpperInvariant() is "JPY" or "HUF" or "TWD";
        return zeroDecimal
            ? decimal.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)
            : decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static (decimal value, string currency)? ReadMoney(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var m) || m.ValueKind != JsonValueKind.Object)
            return null;
        var valueStr = GetStringOrNull(m, "value");
        if (valueStr is null || !decimal.TryParse(valueStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            return null;
        var currency = GetStringOrNull(m, "currency_code") ?? string.Empty;
        return (value, currency);
    }

    private static DateTimeOffset? ReadDateTime(JsonElement parent, string propertyName)
    {
        var s = GetStringOrNull(parent, propertyName);
        if (s is null) return null;
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt : null;
    }

    private static string? GetStringOrNull(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static JsonElement? TryGetFirstAuthorization(JsonElement orderRoot)
    {
        if (!orderRoot.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments) &&
                payments.TryGetProperty("authorizations", out var auths) &&
                auths.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in auths.EnumerateArray())
                    return a;
            }
        }
        return null;
    }

    private static string FormatRfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}

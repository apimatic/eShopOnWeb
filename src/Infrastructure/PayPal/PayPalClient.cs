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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Talks to PayPal's REST API directly (Orders v2, Payments v2, Vault v3, Transaction Search v1), which is
/// the integration surface the PayPal plugin documents and sanctions for full control over request structure.
/// Card details flow through this type only in memory and are never logged.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalClient> _logger;

    // Access-token cache shared across instances of this typed client within the process.
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? _cachedToken;
    private static DateTimeOffset _cachedTokenExpiry = DateTimeOffset.MinValue;
    private static string? _cachedTokenKey;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public PayPalClient(HttpClient http, PayPalSettings settings, IAppLogger<PayPalClient> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
    }

    private string BaseUrl => _settings.ResolveBaseUrl();

    // ---------------------------------------------------------------------- Authorize

    public Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card,
        string invoiceId, string requestId, CancellationToken cancellationToken = default)
    {
        var cardNode = BuildCardNode(card);
        return CreateAuthorizedOrderAsync(amount, currency, invoiceId, requestId, cardNode, cancellationToken);
    }

    public Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultId,
        string invoiceId, string requestId, CancellationToken cancellationToken = default)
    {
        var cardNode = new Dictionary<string, object?> { ["vault_id"] = vaultId };
        return CreateAuthorizedOrderAsync(amount, currency, invoiceId, requestId, cardNode, cancellationToken);
    }

    private async Task<AuthorizationResult> CreateAuthorizedOrderAsync(decimal amount, string currency,
        string invoiceId, string requestId, Dictionary<string, object?> cardNode, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["invoice_id"] = invoiceId,
                    ["custom_id"] = invoiceId,
                    ["amount"] = new Dictionary<string, object?>
                    {
                        ["currency_code"] = currency,
                        ["value"] = Money(amount)
                    }
                }
            },
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = cardNode
            }
        };

        using var doc = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId, cancellationToken);
        var root = doc.RootElement;

        var orderId = root.GetProperty("id").GetString()!;
        var orderStatus = GetString(root, "status");

        // A card that triggers a browser challenge cannot be completed server-to-server — surface it, do not work around it.
        if (string.Equals(orderStatus, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalApiException((int)HttpStatusCode.UnprocessableEntity, "PAYER_ACTION_REQUIRED", GetDebugId(root),
                "PayPal requires the shopper to approve this card payment in a browser (3-D Secure challenge). " +
                "This integration only supports direct card payments that complete without an approval round-trip.");
        }

        var authorization = FindFirstAuthorization(root)
            ?? throw new PayPalApiException((int)HttpStatusCode.UnprocessableEntity, "NO_AUTHORIZATION", GetDebugId(root),
                $"PayPal did not return an authorization for order {orderId} (status '{orderStatus}').");

        var authId = authorization.GetProperty("id").GetString()!;
        var authStatus = GetString(authorization, "status") ?? "CREATED";
        var expiresAt = GetDateTime(authorization, "expiration_time");

        string? brand = null, lastDigits = null, cardExpiry = null, name = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var c))
        {
            brand = GetString(c, "brand");
            lastDigits = GetString(c, "last_digits");
            cardExpiry = GetString(c, "expiry");
            name = GetString(c, "name");
        }

        return new AuthorizationResult(orderId, authId, authStatus, expiresAt, brand, lastDigits, cardExpiry, name);
    }

    // ---------------------------------------------------------------------- Get / reauthorize / capture / void

    public async Task<AuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        var root = doc.RootElement;
        return new AuthorizationState(
            authorizationId,
            GetString(root, "status") ?? "UNKNOWN",
            GetDateTime(root, "expiration_time"));
    }

    public async Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = new Dictionary<string, object?>
            {
                ["currency_code"] = currency,
                ["value"] = Money(amount)
            }
        };
        using var doc = await SendJsonAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body, Guid.NewGuid().ToString("N"), cancellationToken);
        var root = doc.RootElement;
        return new ReauthorizationResult(
            root.GetProperty("id").GetString()!,
            GetString(root, "status") ?? "CREATED",
            GetDateTime(root, "expiration_time"));
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string invoiceId, string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["final_capture"] = true,
            ["invoice_id"] = invoiceId
        };
        using var doc = await SendJsonAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture",
            body, requestId, cancellationToken);
        var root = doc.RootElement;

        var captureId = root.GetProperty("id").GetString()!;
        var status = GetString(root, "status") ?? "COMPLETED";

        decimal gross = 0m, fee = 0m, net = 0m;
        var currency = _settings.Currency;
        if (root.TryGetProperty("seller_receivable_breakdown", out var b))
        {
            gross = MoneyValue(b, "gross_amount", out currency);
            fee = MoneyValue(b, "paypal_fee", out _);
            net = MoneyValue(b, "net_amount", out _);
        }
        else if (root.TryGetProperty("amount", out var amt))
        {
            gross = ParseDecimal(GetString(amt, "value"));
            net = gross;
            currency = GetString(amt, "currency_code") ?? currency;
        }

        return new CaptureResult(captureId, status, gross, fee, net, currency);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var response = await SendRawAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void",
            null, Guid.NewGuid().ToString("N"), cancellationToken);
        // 204 No Content on success; a 422 for an already-voided authorization is treated as success (idempotent intent).
        if (response.StatusCode == HttpStatusCode.NoContent || response.IsSuccessStatusCode)
        {
            return;
        }
        await ThrowFromErrorAsync(response, cancellationToken);
    }

    // ---------------------------------------------------------------------- Refund

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string invoiceId,
        string requestId, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["invoice_id"] = invoiceId };
        if (amount.HasValue)
        {
            body["amount"] = new Dictionary<string, object?>
            {
                ["currency_code"] = currency,
                ["value"] = Money(amount.Value)
            };
        }

        using var doc = await SendJsonAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund",
            body, requestId, cancellationToken);
        var root = doc.RootElement;

        var refundId = root.GetProperty("id").GetString()!;
        var status = GetString(root, "status") ?? "COMPLETED";
        var value = amount ?? 0m;
        var cur = currency;
        if (root.TryGetProperty("amount", out var amt))
        {
            value = ParseDecimal(GetString(amt, "value"));
            cur = GetString(amt, "currency_code") ?? currency;
        }
        return new RefundResult(refundId, status, value, cur);
    }

    // ---------------------------------------------------------------------- Vault

    public async Task<VaultResult> VaultCardAsync(CardDetails card, string? customerId, string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = BuildCardNode(card) }
        };
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            body["customer"] = new Dictionary<string, object?> { ["id"] = customerId };
        }

        using var doc = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, requestId, cancellationToken);
        var root = doc.RootElement;

        var vaultId = root.GetProperty("id").GetString()!;
        var returnedCustomerId = root.TryGetProperty("customer", out var cust) ? GetString(cust, "id") ?? "" : "";

        string brand = "", lastDigits = "", expiry = "", name = card.CardholderName;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var c))
        {
            brand = GetString(c, "brand") ?? "";
            lastDigits = GetString(c, "last_digits") ?? "";
            expiry = GetString(c, "expiry") ?? "";
            name = GetString(c, "name") ?? card.CardholderName;
        }

        return new VaultResult(vaultId, returnedCustomerId, brand, lastDigits, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        var response = await SendRawAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent || response.IsSuccessStatusCode
            || response.StatusCode == HttpStatusCode.NotFound)
        {
            return; // already gone counts as deleted
        }
        await ThrowFromErrorAsync(response, cancellationToken);
    }

    // ---------------------------------------------------------------------- Reporting / reconciliation

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();
        var seen = new HashSet<string>();

        // PayPal's Transaction Search allows at most a 31-day window per call; walk the range in 30-day chunks.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(30);
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            var totalPages = 1;
            do
            {
                var path = "/v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(ReportDate(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(ReportDate(windowEnd))}" +
                    "&fields=all&page_size=500" +
                    $"&page={page}";

                using var doc = await SendJsonAsync(HttpMethod.Get, path, null, null, cancellationToken);
                var root = doc.RootElement;

                totalPages = root.TryGetProperty("total_pages", out var tp) && tp.ValueKind == JsonValueKind.Number
                    ? tp.GetInt32() : 1;

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d in details.EnumerateArray())
                    {
                        if (!d.TryGetProperty("transaction_info", out var info)) continue;
                        var txId = GetString(info, "transaction_id");
                        if (txId is null || !seen.Add(txId)) continue;

                        var amount = MoneyValue(info, "transaction_amount", out var cur);
                        var fee = info.TryGetProperty("fee_amount", out _) ? MoneyValue(info, "fee_amount", out _) : 0m;
                        var date = GetDateTime(info, "transaction_initiation_date") ?? windowStart;

                        results.Add(new PayPalTransaction(
                            txId,
                            GetString(info, "transaction_status") ?? "",
                            GetString(info, "transaction_event_code"),
                            amount,
                            fee,
                            cur,
                            date,
                            GetString(info, "invoice_id"),
                            GetString(info, "custom_field")));
                    }
                }

                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd;
        }

        return results;
    }

    // ---------------------------------------------------------------------- HTTP plumbing

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken)
    {
        var response = await SendRawAsync(method, path, body, requestId, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, content);
        }
        return string.IsNullOrWhiteSpace(content) ? JsonDocument.Parse("{}") : JsonDocument.Parse(content);
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await _http.SendAsync(request, cancellationToken);
    }

    private async Task ThrowFromErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        throw BuildException(response.StatusCode, content);
    }

    private PayPalApiException BuildException(HttpStatusCode statusCode, string content)
    {
        string? issue = null, debugId = null, message = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
            var root = doc.RootElement;
            debugId = GetString(root, "debug_id");
            message = GetString(root, "message") ?? GetString(root, "error_description") ?? GetString(root, "name");
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
                && details.GetArrayLength() > 0)
            {
                var first = details[0];
                issue = GetString(first, "issue");
                var desc = GetString(first, "description");
                if (!string.IsNullOrEmpty(desc)) message = desc;
            }
            issue ??= GetString(root, "name");
        }
        catch (JsonException)
        {
            message = content;
        }

        var text = $"PayPal {(int)statusCode} {issue ?? "error"}: {message ?? "no detail"}" +
                   (debugId is not null ? $" (debug_id {debugId})" : "");
        _logger.LogWarning("PayPal API error: {0}", text);
        return new PayPalApiException((int)statusCode, issue, debugId, text);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var key = BaseUrl + "|" + _settings.ClientId;
        if (_cachedToken is not null && _cachedTokenKey == key && DateTimeOffset.UtcNow < _cachedTokenExpiry)
        {
            return _cachedToken;
        }

        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && _cachedTokenKey == key && DateTimeOffset.UtcNow < _cachedTokenExpiry)
            {
                return _cachedToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            var response = await _http.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw BuildException(response.StatusCode, content);
            }

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var token = root.GetProperty("access_token").GetString()!;
            var expiresIn = root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number
                ? e.GetInt32() : 3000;

            _cachedToken = token;
            _cachedTokenKey = key;
            _cachedTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn - 60));
            return token;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    // ---------------------------------------------------------------------- helpers

    private static Dictionary<string, object?> BuildCardNode(CardDetails card)
    {
        var node = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["name"] = card.CardholderName
        };

        if (card.BillingAddress is { } a)
        {
            var address = new Dictionary<string, object?>
            {
                ["address_line_1"] = a.AddressLine1,
                ["address_line_2"] = a.AddressLine2,
                ["admin_area_2"] = a.AdminArea2,
                ["admin_area_1"] = a.AdminArea1,
                ["postal_code"] = a.PostalCode,
                ["country_code"] = a.CountryCode
            };
            node["billing_address"] = address;
        }

        return node;
    }

    private static JsonElement? FindFirstAuthorization(JsonElement orderRoot)
    {
        if (!orderRoot.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments)
                && payments.TryGetProperty("authorizations", out var auths)
                && auths.ValueKind == JsonValueKind.Array && auths.GetArrayLength() > 0)
            {
                return auths[0];
            }
        }
        return null;
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string ReportDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "Z";

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static string? GetDebugId(JsonElement root) => GetString(root, "debug_id");

    private static DateTimeOffset? GetDateTime(JsonElement element, string name)
    {
        var s = GetString(element, name);
        return s is not null && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt) ? dt : null;
    }

    private static decimal MoneyValue(JsonElement parent, string name, out string currency)
    {
        currency = "USD";
        if (parent.TryGetProperty(name, out var money) && money.ValueKind == JsonValueKind.Object)
        {
            currency = GetString(money, "currency_code") ?? "USD";
            return ParseDecimal(GetString(money, "value"));
        }
        return 0m;
    }

    private static decimal ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
}

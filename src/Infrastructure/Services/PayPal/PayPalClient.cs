using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// A production-grade gateway over the PayPal REST APIs (Orders v2, Payments v2, Vault v3,
/// Transaction Search v1). Owns OAuth token acquisition/caching, base-URL resolution, request
/// idempotency headers and translation of PayPal errors into domain exceptions.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalClient> _logger;
    private readonly string _baseUrl;

    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? _cachedToken;
    private static DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    // Transaction Search allows at most a 31-day window per request.
    private static readonly TimeSpan MaxTransactionWindow = TimeSpan.FromDays(31);

    public PayPalClient(HttpClient httpClient, IOptions<PayPalSettings> settings, IAppLogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _baseUrl = _settings.ResolveBaseUrl();
    }

    // ---------------------------------------------------------------------------------------------
    // Orders v2 — authorize
    // ---------------------------------------------------------------------------------------------

    public async Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(
        PayPalMoney amount, string invoiceId, string customId, PayPalCard card, string requestId, CancellationToken cancellationToken = default)
    {
        var body = BuildAuthorizeOrderBody(amount, invoiceId, customId, new JsonObject
        {
            ["card"] = BuildCardNode(card)
        });
        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId, representation: true, cancellationToken);
        return ParseAuthorization(doc.RootElement);
    }

    public async Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(
        PayPalMoney amount, string invoiceId, string customId, string vaultTokenId, string requestId, CancellationToken cancellationToken = default)
    {
        var body = BuildAuthorizeOrderBody(amount, invoiceId, customId, new JsonObject
        {
            ["card"] = new JsonObject { ["vault_id"] = vaultTokenId }
        });
        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId, representation: true, cancellationToken);
        return ParseAuthorization(doc.RootElement);
    }

    private static JsonObject BuildAuthorizeOrderBody(PayPalMoney amount, string invoiceId, string customId, JsonObject paymentSource)
    {
        return new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["reference_id"] = "default",
                    ["invoice_id"] = invoiceId,
                    ["custom_id"] = customId,
                    ["amount"] = MoneyNode(amount)
                }
            },
            ["payment_source"] = paymentSource
        };
    }

    private static PayPalAuthorizationResult ParseAuthorization(JsonElement root)
    {
        var orderId = root.GetProperty("id").GetString()!;
        string? brand = null, last4 = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            brand = GetString(cardEl, "brand");
            last4 = GetString(cardEl, "last_digits");
        }

        if (!root.TryGetProperty("purchase_units", out var units) || units.GetArrayLength() == 0)
        {
            throw new PayPalGatewayException("PayPal order response contained no purchase units.");
        }
        var payments = units[0].TryGetProperty("payments", out var pay) ? pay : default;
        if (payments.ValueKind != JsonValueKind.Object || !payments.TryGetProperty("authorizations", out var auths) || auths.GetArrayLength() == 0)
        {
            // The order was created but not authorized — surface the order status so an operator can see why.
            var status = GetString(root, "status") ?? "UNKNOWN";
            throw new PayPalGatewayException($"PayPal did not authorize the payment (order status '{status}'). No approval round-trip is supported.");
        }

        var auth = auths[0];
        return new PayPalAuthorizationResult(
            orderId,
            auth.GetProperty("id").GetString()!,
            GetString(auth, "status") ?? "UNKNOWN",
            GetDate(auth, "expiration_time"),
            brand,
            last4);
    }

    // ---------------------------------------------------------------------------------------------
    // Payments v2 — get / reauthorize / capture / void / refund
    // ---------------------------------------------------------------------------------------------

    public async Task<PayPalAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, representation: false, cancellationToken);
        var root = doc.RootElement;
        return new PayPalAuthorizationState(GetString(root, "status") ?? "UNKNOWN", GetDate(root, "expiration_time"));
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId, PayPalMoney amount, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["amount"] = MoneyNode(amount) };
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, requestId, representation: true, cancellationToken);
        var root = doc.RootElement;
        // Reauthorization returns a fresh authorization (its own id + new expiry).
        return new PayPalAuthorizationResult(
            string.Empty,
            GetString(root, "id") ?? authorizationId,
            GetString(root, "status") ?? "UNKNOWN",
            GetDate(root, "expiration_time"),
            null,
            null);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["final_capture"] = true };
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, requestId, representation: true, cancellationToken);
        var root = doc.RootElement;

        var captureId = root.GetProperty("id").GetString()!;
        var status = GetString(root, "status") ?? "UNKNOWN";

        decimal gross = 0m, fee = 0m, net = 0m;
        if (root.TryGetProperty("seller_receivable_breakdown", out var b))
        {
            gross = GetMoney(b, "gross_amount");
            fee = GetMoney(b, "paypal_fee");
            net = GetMoney(b, "net_amount");
        }
        else if (root.TryGetProperty("amount", out _))
        {
            gross = GetMoney(root, "amount");
            net = gross;
        }

        return new PayPalCaptureResult(captureId, status, gross, fee, net);
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, requestId, representation: false, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundAsync(
        string captureId, PayPalMoney? amount, string invoiceId, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["invoice_id"] = invoiceId };
        if (amount is not null)
        {
            body["amount"] = MoneyNode(amount);
        }
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, requestId, representation: true, cancellationToken);
        var root = doc.RootElement;
        return new PayPalRefundResult(
            root.GetProperty("id").GetString()!,
            GetString(root, "status") ?? "UNKNOWN",
            root.TryGetProperty("amount", out _) ? GetMoney(root, "amount") : (amount?.Value ?? 0m));
    }

    // ---------------------------------------------------------------------------------------------
    // Vault v3 — save / delete a card
    // ---------------------------------------------------------------------------------------------

    public async Task<PayPalVaultedCard> VaultCardAsync(PayPalCard card, string customerId, string requestId, CancellationToken cancellationToken = default)
    {
        // Step 1: create a setup token from the card.
        var setupBody = new JsonObject
        {
            ["customer"] = new JsonObject { ["id"] = customerId },
            ["payment_source"] = new JsonObject { ["card"] = BuildCardNode(card) }
        };
        using var setupDoc = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody, $"{requestId}-setup", representation: false, cancellationToken);
        var setupRoot = setupDoc.RootElement;
        var setupStatus = GetString(setupRoot, "status") ?? "UNKNOWN";
        if (string.Equals(setupStatus, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalGatewayException(
                "Saving this card requires the shopper to approve it in a browser; no approval round-trip is supported.",
                payPalErrorName: "PAYER_ACTION_REQUIRED");
        }
        var setupTokenId = setupRoot.GetProperty("id").GetString()!;

        // Step 2: exchange the approved setup token for a durable payment token.
        var tokenBody = new JsonObject
        {
            ["payment_source"] = new JsonObject
            {
                ["token"] = new JsonObject { ["id"] = setupTokenId, ["type"] = "SETUP_TOKEN" }
            }
        };
        using var tokenDoc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenBody, $"{requestId}-token", representation: false, cancellationToken);
        var tokenRoot = tokenDoc.RootElement;

        var vaultTokenId = tokenRoot.GetProperty("id").GetString()!;
        string? brand = null, last4 = null, name = null, expiry = null;
        var resolvedCustomerId = customerId;
        if (tokenRoot.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            brand = GetString(cardEl, "brand");
            last4 = GetString(cardEl, "last_digits");
            name = GetString(cardEl, "name");
            expiry = GetString(cardEl, "expiry");
        }
        if (tokenRoot.TryGetProperty("customer", out var cust))
        {
            resolvedCustomerId = GetString(cust, "id") ?? customerId;
        }

        return new PayPalVaultedCard(vaultTokenId, resolvedCustomerId, brand ?? "UNKNOWN", last4 ?? "****", name, expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultTokenId}", null, null, representation: false, cancellationToken);
    }

    // ---------------------------------------------------------------------------------------------
    // Transaction Search v1 — reconciliation
    // ---------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, string? currencyCode, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();

        // The API caps each request at 31 days, so walk the range in windows and page each window fully.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxTransactionWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await ReadTransactionWindowAsync(windowStart, windowEnd, results, cancellationToken);

            windowStart = windowEnd;
        }

        return results;
    }

    private async Task ReadTransactionWindowAsync(
        DateTimeOffset start, DateTimeOffset end, List<PayPalTransaction> sink, CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        int page = 1;
        int totalPages = 1;

        do
        {
            var query =
                $"?start_date={Rfc3339(start)}&end_date={Rfc3339(end)}" +
                $"&fields=transaction_info&balance_affecting_records_only=Y&page_size={pageSize}&page={page}&total_required=true";

            using var doc = await SendAsync(HttpMethod.Get, $"/v1/reporting/transactions{query}", null, null, representation: false, cancellationToken);
            var root = doc.RootElement;

            if (root.TryGetProperty("total_pages", out var tp) && tp.ValueKind == JsonValueKind.Number)
            {
                totalPages = tp.GetInt32();
            }

            if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in details.EnumerateArray())
                {
                    if (!d.TryGetProperty("transaction_info", out var info))
                    {
                        continue;
                    }

                    var currency = "";
                    decimal amount = 0m, fee = 0m;
                    if (info.TryGetProperty("transaction_amount", out var ta))
                    {
                        amount = GetMoneyValue(ta);
                        currency = GetString(ta, "currency_code") ?? "";
                    }

                    sink.Add(new PayPalTransaction(
                        GetString(info, "transaction_id") ?? "",
                        GetString(info, "invoice_id"),
                        GetString(info, "transaction_event_code") ?? "",
                        GetString(info, "transaction_status") ?? "",
                        amount,
                        currency,
                        GetDate(info, "transaction_initiation_date") ?? start));
                }
            }

            page++;
        }
        while (page <= totalPages);
    }

    // ---------------------------------------------------------------------------------------------
    // HTTP plumbing
    // ---------------------------------------------------------------------------------------------

    private async Task<JsonDocument> SendAsync(
        HttpMethod method, string path, JsonNode? body, string? requestId, bool representation, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, _baseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (representation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildGatewayException(method, path, response.StatusCode, payload);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            // 204 No Content (e.g. void / delete) — represent as an empty JSON object.
            return JsonDocument.Parse("{}");
        }

        return JsonDocument.Parse(payload);
    }

    private PayPalGatewayException BuildGatewayException(HttpMethod method, string path, HttpStatusCode status, string payload)
    {
        string? name = null, message = null, debugId = null, issues = null;
        try
        {
            using var err = JsonDocument.Parse(payload);
            var root = err.RootElement;
            name = GetString(root, "name");
            message = GetString(root, "message");
            debugId = GetString(root, "debug_id");
            if (message is null && root.TryGetProperty("error_description", out var ed))
            {
                message = ed.GetString();
                name ??= GetString(root, "error");
            }
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var d in details.EnumerateArray())
                {
                    var issue = GetString(d, "issue");
                    var desc = GetString(d, "description");
                    var field = GetString(d, "field");
                    if (issue is not null)
                    {
                        parts.Add(field is not null ? $"{issue} ({field}): {desc}" : $"{issue}: {desc}");
                    }
                }
                if (parts.Count > 0)
                {
                    issues = string.Join("; ", parts);
                }
            }
        }
        catch (JsonException)
        {
            // non-JSON error body; fall through
        }

        var detail = issues ?? message ?? "no details";
        _logger.LogWarning($"PayPal {method} {path} failed with {(int)status}: {name} {detail} (debug_id={debugId})");
        return new PayPalGatewayException(
            $"PayPal request failed ({(int)status} {name ?? status.ToString()}): {detail}.",
            name,
            debugId);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _cachedToken;
        }

        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _cachedToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw BuildGatewayException(HttpMethod.Post, "/v1/oauth2/token", response.StatusCode, payload);
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var token = root.GetProperty("access_token").GetString()!;
            var expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3000;

            _cachedToken = token;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn - 60));
            return token;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // JSON helpers
    // ---------------------------------------------------------------------------------------------

    private static JsonObject BuildCardNode(PayPalCard card)
    {
        var node = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode
        };
        if (!string.IsNullOrWhiteSpace(card.Name))
        {
            node["name"] = card.Name;
        }
        if (card.BillingAddress is not null)
        {
            var a = card.BillingAddress;
            var addr = new JsonObject { ["country_code"] = a.CountryCode };
            if (a.AddressLine1 is not null) addr["address_line_1"] = a.AddressLine1;
            if (a.AdminArea2 is not null) addr["admin_area_2"] = a.AdminArea2;
            if (a.AdminArea1 is not null) addr["admin_area_1"] = a.AdminArea1;
            if (a.PostalCode is not null) addr["postal_code"] = a.PostalCode;
            node["billing_address"] = addr;
        }
        return node;
    }

    private static JsonObject MoneyNode(PayPalMoney money) => new()
    {
        ["currency_code"] = money.CurrencyCode,
        ["value"] = money.Value.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static string Rfc3339(DateTimeOffset value) =>
        Uri.EscapeDataString(value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static DateTimeOffset? GetDate(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
            ? d
            : null;

    private static decimal GetMoney(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var m) ? GetMoneyValue(m) : 0m;

    private static decimal GetMoneyValue(JsonElement moneyElement) =>
        moneyElement.ValueKind == JsonValueKind.Object && moneyElement.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String
            && decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : 0m;
}

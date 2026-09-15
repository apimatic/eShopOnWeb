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
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal REST API client. Uses plain HTTP against the documented v2 Orders/Payments, v3 Vault, and
/// v1 Reporting endpoints. Amounts are always formatted to two decimals in the invariant culture so
/// the value sent equals the order total to the cent. Card details flow through this client only as
/// transient request bodies — nothing card-related is logged or persisted.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly PayPalTokenProvider _tokenProvider;
    private readonly ILogger<PayPalClient> _logger;
    private readonly string _baseUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public PayPalClient(HttpClient http, PayPalSettings settings, PayPalTokenProvider tokenProvider,
        ILogger<PayPalClient> logger)
    {
        _http = http;
        _settings = settings;
        _tokenProvider = tokenProvider;
        _logger = logger;
        _baseUrl = settings.ResolveBaseUrl();
    }

    // ---------- Orders / authorizations ----------

    public Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var paymentSource = new Dictionary<string, object?> { ["card"] = BuildCardObject(card) };
        return CreateAndAuthorizeAsync(amount, currency, paymentSource, idempotencyKey, cancellationToken);
    }

    public Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var paymentSource = new Dictionary<string, object?>
        {
            ["card"] = new Dictionary<string, object?> { ["vault_id"] = vaultId }
        };
        return CreateAndAuthorizeAsync(amount, currency, paymentSource, idempotencyKey, cancellationToken);
    }

    private async Task<AuthorizationResult> CreateAndAuthorizeAsync(decimal amount, string currency,
        object paymentSource, string idempotencyKey, CancellationToken cancellationToken)
    {
        var createBody = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new Dictionary<string, object?> { ["amount"] = Money(amount, currency) }
            },
            ["payment_source"] = paymentSource
        };

        using var created = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", createBody,
            $"{idempotencyKey}-create", cancellationToken);
        var createdRoot = created!.RootElement;
        var payPalOrderId = createdRoot.GetProperty("id").GetString()!;

        GuardAgainstBuyerApproval(createdRoot);

        // The card/vault is attached at creation; the authorization may already be present. If not,
        // explicitly authorize the order.
        var authorization = TryReadAuthorization(createdRoot);
        if (authorization is null)
        {
            using var authorized = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize",
                new Dictionary<string, object?>(), $"{idempotencyKey}-auth", cancellationToken);
            var authorizedRoot = authorized!.RootElement;
            GuardAgainstBuyerApproval(authorizedRoot);
            authorization = TryReadAuthorization(authorizedRoot);
        }

        if (authorization is null)
            throw new PayPalApiException(502, null, "PayPal did not return an authorization for the order.");

        return new AuthorizationResult(payPalOrderId, authorization.Value.Id, authorization.Value.Status);
    }

    public async Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null,
            null, cancellationToken);
        var root = doc!.RootElement;
        return new AuthorizationDetails(root.GetProperty("id").GetString()!, root.GetProperty("status").GetString()!);
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["final_capture"] = true };
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture",
            body, idempotencyKey, cancellationToken);
        var root = doc!.RootElement;

        var captureId = root.GetProperty("id").GetString()!;
        var status = root.GetProperty("status").GetString()!;

        decimal gross = 0m, fee = 0m, net = 0m;
        var currency = _settings.Currency;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            (gross, currency) = ReadMoney(breakdown, "gross_amount") ?? (gross, currency);
            fee = ReadMoney(breakdown, "paypal_fee")?.Amount ?? 0m;
            net = ReadMoney(breakdown, "net_amount")?.Amount ?? gross - fee;
        }

        return new CaptureResult(captureId, status, gross, fee, net, currency);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null,
            null, cancellationToken);
    }

    public async Task<AuthorizationDetails> ReauthorizeAuthorizationAsync(string authorizationId, decimal amount,
        string currency, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["amount"] = Money(amount, currency) };
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body, null, cancellationToken);
        var root = doc!.RootElement;
        return new AuthorizationDetails(root.GetProperty("id").GetString()!, root.GetProperty("status").GetString()!);
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>();
        if (amount is not null)
            body["amount"] = Money(amount.Value, currency);

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body,
            idempotencyKey, cancellationToken);
        var root = doc!.RootElement;
        return new RefundResult(root.GetProperty("id").GetString()!, root.GetProperty("status").GetString()!);
    }

    // ---------- Vault (saved cards) ----------

    public async Task<VaultedCard> VaultCardAsync(CardDetails card, string? customerId,
        CancellationToken cancellationToken = default)
    {
        // 1) Create a setup token for the card (no purchase, no browser approval for direct-card merchants).
        var setupBody = new Dictionary<string, object?> { ["payment_source"] = new Dictionary<string, object?> { ["card"] = BuildCardObject(card) } };
        if (!string.IsNullOrEmpty(customerId))
            setupBody["customer"] = new Dictionary<string, object?> { ["id"] = customerId };

        using var setupDoc = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody, null, cancellationToken);
        var setupRoot = setupDoc!.RootElement;
        GuardAgainstBuyerApproval(setupRoot);
        var setupTokenId = setupRoot.GetProperty("id").GetString()!;

        // 2) Exchange the setup token for a permanent payment (vault) token.
        var tokenBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?> { ["id"] = setupTokenId, ["type"] = "SETUP_TOKEN" }
            }
        };

        using var tokenDoc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenBody, null, cancellationToken);
        var tokenRoot = tokenDoc!.RootElement;

        var vaultId = tokenRoot.GetProperty("id").GetString()!;
        string? resolvedCustomerId = customerId;
        if (tokenRoot.TryGetProperty("customer", out var customer) && customer.TryGetProperty("id", out var cid))
            resolvedCustomerId = cid.GetString();

        var brand = "CARD";
        var last4 = "0000";
        string? expiry = card.Expiry;
        string? name = card.CardholderName;
        if (tokenRoot.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            brand = cardEl.TryGetProperty("brand", out var b) ? b.GetString() ?? brand : brand;
            last4 = cardEl.TryGetProperty("last_digits", out var l) ? l.GetString() ?? last4 : last4;
            expiry = cardEl.TryGetProperty("expiry", out var e) ? e.GetString() ?? expiry : expiry;
            name = cardEl.TryGetProperty("name", out var n) ? n.GetString() ?? name : name;
        }

        return new VaultedCard(vaultId, resolvedCustomerId, brand, last4, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, cancellationToken);
    }

    // ---------- Reporting / reconciliation ----------

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, GatewayTransaction>();

        // Transaction Search allows at most a 31-day range per call, so walk the range in windows.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to) windowEnd = to;

            await CollectWindowAsync(windowStart, windowEnd, results, cancellationToken);

            windowStart = windowEnd;
        }

        return results.Values.ToList();
    }

    private async Task CollectWindowAsync(DateTimeOffset start, DateTimeOffset end,
        Dictionary<string, GatewayTransaction> results, CancellationToken cancellationToken)
    {
        var startStr = Uri.EscapeDataString(start.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture));
        var endStr = Uri.EscapeDataString(end.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture));

        var page = 1;
        var totalPages = 1;
        do
        {
            var path = $"/v1/reporting/transactions?start_date={startStr}&end_date={endStr}&fields=all&page_size=500&page={page}";
            using var doc = await SendAsync(HttpMethod.Get, path, null, null, cancellationToken);
            var root = doc!.RootElement;

            if (root.TryGetProperty("total_pages", out var tp))
                totalPages = tp.GetInt32();

            if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (!detail.TryGetProperty("transaction_info", out var info)) continue;
                    var tx = ReadTransaction(info);
                    if (tx is not null)
                        results[tx.TransactionId] = tx;
                }
            }

            page++;
        }
        while (page <= totalPages);
    }

    private static GatewayTransaction? ReadTransaction(JsonElement info)
    {
        if (!info.TryGetProperty("transaction_id", out var idEl)) return null;
        var id = idEl.GetString();
        if (string.IsNullOrEmpty(id)) return null;

        var status = info.TryGetProperty("transaction_status", out var s) ? s.GetString() ?? "" : "";
        var eventCode = info.TryGetProperty("transaction_event_code", out var ec) ? ec.GetString() : null;

        decimal amount = 0m;
        var currency = "";
        if (info.TryGetProperty("transaction_amount", out var amt))
        {
            if (amt.TryGetProperty("value", out var v) && decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                amount = parsed;
            currency = amt.TryGetProperty("currency_code", out var c) ? c.GetString() ?? "" : "";
        }

        var date = DateTimeOffset.MinValue;
        if (info.TryGetProperty("transaction_initiation_date", out var d) && d.ValueKind == JsonValueKind.String)
            DateTimeOffset.TryParse(d.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date);

        return new GatewayTransaction(id, status, amount, currency, date, eventCode);
    }

    // ---------- Helpers ----------

    private static Dictionary<string, object?> BuildCardObject(CardDetails card)
    {
        var cardObj = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry
        };
        if (!string.IsNullOrEmpty(card.SecurityCode)) cardObj["security_code"] = card.SecurityCode;
        if (!string.IsNullOrEmpty(card.CardholderName)) cardObj["name"] = card.CardholderName;

        var billing = new Dictionary<string, object?>();
        if (!string.IsNullOrEmpty(card.BillingAddressLine1)) billing["address_line_1"] = card.BillingAddressLine1;
        if (!string.IsNullOrEmpty(card.BillingAddressLine2)) billing["address_line_2"] = card.BillingAddressLine2;
        if (!string.IsNullOrEmpty(card.BillingCity)) billing["admin_area_2"] = card.BillingCity;
        if (!string.IsNullOrEmpty(card.BillingState)) billing["admin_area_1"] = card.BillingState;
        if (!string.IsNullOrEmpty(card.BillingPostalCode)) billing["postal_code"] = card.BillingPostalCode;
        if (!string.IsNullOrEmpty(card.BillingCountryCode)) billing["country_code"] = card.BillingCountryCode;
        if (billing.Count > 0) cardObj["billing_address"] = billing;

        return cardObj;
    }

    private static Dictionary<string, object?> Money(decimal amount, string currency) => new()
    {
        ["currency_code"] = currency,
        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static (decimal Amount, string Currency)? ReadMoney(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var money)) return null;
        var currency = money.TryGetProperty("currency_code", out var c) ? c.GetString() ?? "" : "";
        if (money.TryGetProperty("value", out var v) &&
            decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
        {
            return (amount, currency);
        }
        return null;
    }

    private static (string Id, string Status)? TryReadAuthorization(JsonElement orderRoot)
    {
        if (!orderRoot.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments) &&
                payments.TryGetProperty("authorizations", out var auths) &&
                auths.ValueKind == JsonValueKind.Array)
            {
                foreach (var auth in auths.EnumerateArray())
                {
                    var id = auth.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    var status = auth.TryGetProperty("status", out var stEl) ? stEl.GetString() : null;
                    if (!string.IsNullOrEmpty(id))
                        return (id!, status ?? "");
                }
            }
        }
        return null;
    }

    /// <summary>
    /// If PayPal signals that the shopper must approve the payment in a browser, stop and report it
    /// rather than building an approval round-trip.
    /// </summary>
    private static void GuardAgainstBuyerApproval(JsonElement root)
    {
        var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentApprovalRequiredException(
                "PayPal requires the shopper to approve this payment in a browser (payer action required). " +
                "This integration does not support a browser approval step.");
        }

        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                var rel = link.TryGetProperty("rel", out var r) ? r.GetString() : null;
                if (string.Equals(rel, "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    throw new PaymentApprovalRequiredException(
                        "PayPal returned a payer-action link, meaning the shopper must approve this payment in a " +
                        "browser. This integration does not support a browser approval step.");
                }
            }
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        return await _tokenProvider.GetAsync(async ct =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });

            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new PayPalApiException((int)response.StatusCode, null, "Failed to obtain a PayPal access token.", body);

            using var doc = JsonDocument.Parse(body);
            var token = doc.RootElement.GetProperty("access_token").GetString()!;
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3000;
            return (token, expiresIn);
        }, cancellationToken);
    }

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, $"{_baseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(requestId))
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (issue, message) = ParseError(responseBody);
            _logger.LogWarning("PayPal {Method} {Path} failed: {Status} {Issue} {Message}",
                method, path, (int)response.StatusCode, issue, message);
            throw new PayPalApiException((int)response.StatusCode, issue,
                message ?? $"PayPal request failed with status {(int)response.StatusCode}.", responseBody);
        }

        if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(responseBody))
            return null;

        return JsonDocument.Parse(responseBody);
    }

    private static (string? Issue, string? Message) ParseError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            string? issue = name;
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                var first = details.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object)
                {
                    if (first.TryGetProperty("issue", out var iss)) issue = iss.GetString() ?? name;
                    if (first.TryGetProperty("description", out var desc) && !string.IsNullOrEmpty(desc.GetString()))
                        message = $"{message} {desc.GetString()}".Trim();
                }
            }
            return (issue, message);
        }
        catch (JsonException)
        {
            return (null, body);
        }
    }
}

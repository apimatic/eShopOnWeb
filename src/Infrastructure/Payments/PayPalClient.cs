using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal REST API client covering the endpoints this integration uses: Orders v2 (authorize), Payments v2
/// (capture/reauthorize/void/refund), Payment Method Tokens v3 (Vault) and Transaction Search v1
/// (reconciliation). Requests are snake_case JSON; card-bearing request bodies are never logged.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly PayPalTokenProvider _tokenProvider;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalClient> _logger;

    public PayPalClient(HttpClient http, PayPalTokenProvider tokenProvider, PayPalSettings settings, ILogger<PayPalClient> logger)
    {
        _http = http;
        _tokenProvider = tokenProvider;
        _settings = settings;
        _logger = logger;
    }

    // ---------------------------------------------------------------- Orders (authorize)

    public Task<AuthorizeResult> AuthorizeOrderWithCardAsync(Money amount, string referenceId, string invoiceId,
        string customId, CardDetails card, string requestId, CancellationToken ct = default)
    {
        var cardNode = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["name"] = card.CardholderName
        };
        AddBillingAddress(cardNode, card.BillingAddress);
        return AuthorizeAsync(amount, referenceId, invoiceId, customId, cardNode, requestId, ct);
    }

    public Task<AuthorizeResult> AuthorizeOrderWithVaultedCardAsync(Money amount, string referenceId, string invoiceId,
        string customId, string vaultId, string requestId, CancellationToken ct = default)
    {
        var cardNode = new Dictionary<string, object?> { ["vault_id"] = vaultId };
        return AuthorizeAsync(amount, referenceId, invoiceId, customId, cardNode, requestId, ct);
    }

    private async Task<AuthorizeResult> AuthorizeAsync(Money amount, string referenceId, string invoiceId,
        string customId, Dictionary<string, object?> cardNode, string requestId, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["reference_id"] = referenceId,
                    ["custom_id"] = customId,
                    ["invoice_id"] = invoiceId,
                    ["amount"] = MoneyNode(amount)
                }
            },
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = cardNode
            }
        };

        using var doc = await SendAsync(HttpMethod.Post, "v2/checkout/orders", payload, requestId, prefer: "return=representation", ct);
        var root = doc.RootElement;

        var payPalOrderId = Str(root, "id") ?? throw ProtocolError("order response missing id");
        var orderStatus = Str(root, "status") ?? "UNKNOWN";

        if (!TryGetAuthorization(root, out var authorization))
        {
            // A card that needs a browser challenge (3DS) yields a payer-action link and no authorization.
            // This headless integration does not build an approval round-trip.
            if (HasPayerActionLink(root))
            {
                throw new PayPalApiException(
                    "The card requires additional buyer authentication in a browser, which this integration does not support.",
                    422, issueCode: "PAYER_ACTION_REQUIRED");
            }

            throw new PayPalApiException($"PayPal did not return an authorization (order status {orderStatus}).", 422);
        }

        var authId = Str(authorization, "id") ?? throw ProtocolError("authorization missing id");
        var authStatus = Str(authorization, "status") ?? "CREATED";
        var expiresAt = ParseDate(Str(authorization, "expiration_time"));

        string? brand = null, lastDigits = null, cardExpiry = null;
        if (TryProp(root, "payment_source", out var ps) && TryProp(ps, "card", out var cardEl))
        {
            brand = Str(cardEl, "brand");
            lastDigits = Str(cardEl, "last_digits");
            cardExpiry = Str(cardEl, "expiry");
        }

        return new AuthorizeResult(payPalOrderId, authId, authStatus, expiresAt, brand, lastDigits, cardExpiry);
    }

    // ---------------------------------------------------------------- Payments (capture/void/reauthorize)

    public async Task<string> GetAuthorizationStatusAsync(string authorizationId, CancellationToken ct = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"v2/payments/authorizations/{authorizationId}", null, null, null, ct);
        return Str(doc.RootElement, "status") ?? "UNKNOWN";
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, Money amount, string invoiceId,
        string customId, string requestId, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["amount"] = MoneyNode(amount),
            ["invoice_id"] = invoiceId,
            ["custom_id"] = customId,
            ["final_capture"] = true
        };

        using var doc = await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/capture",
            payload, requestId, prefer: "return=representation", ct);
        var root = doc.RootElement;

        var captureId = Str(root, "id") ?? throw ProtocolError("capture response missing id");
        var status = Str(root, "status") ?? "COMPLETED";

        decimal gross = amount.Value, fee = 0m, net = amount.Value;
        if (TryProp(root, "seller_receivable_breakdown", out var breakdown))
        {
            gross = MoneyValue(breakdown, "gross_amount") ?? gross;
            fee = MoneyValue(breakdown, "paypal_fee") ?? fee;
            net = MoneyValue(breakdown, "net_amount") ?? net;
        }

        return new CaptureResult(captureId, status, gross, fee, net);
    }

    public async Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, Money amount, string requestId, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?> { ["amount"] = MoneyNode(amount) };

        using var doc = await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/reauthorize",
            payload, requestId, prefer: "return=representation", ct);
        var root = doc.RootElement;

        var newAuthId = Str(root, "id") ?? authorizationId;
        var status = Str(root, "status") ?? "CREATED";
        var expiresAt = ParseDate(Str(root, "expiration_time"));
        return new ReauthorizeResult(newAuthId, status, expiresAt);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void", null, null, null, ct);
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, Money? amount, string invoiceId, string? noteToPayer,
        string requestId, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["invoice_id"] = invoiceId,
            ["note_to_payer"] = noteToPayer
        };
        if (amount is not null)
        {
            payload["amount"] = MoneyNode(amount);
        }

        using var doc = await SendAsync(HttpMethod.Post, $"v2/payments/captures/{captureId}/refund",
            payload, requestId, prefer: "return=representation", ct);
        var root = doc.RootElement;

        var refundId = Str(root, "id") ?? throw ProtocolError("refund response missing id");
        var status = Str(root, "status") ?? "COMPLETED";
        var refundedAmount = MoneyValue(root, "amount") ?? amount?.Value ?? 0m;
        return new RefundResult(refundId, status, refundedAmount);
    }

    // ---------------------------------------------------------------- Vault (save cards)

    public async Task<VaultedCardResult> VaultCardAsync(CardDetails card, string? customerId, string requestId, CancellationToken ct = default)
    {
        var cardNode = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["name"] = card.CardholderName
        };
        AddBillingAddress(cardNode, card.BillingAddress);

        var setupPayload = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = cardNode }
        };
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            setupPayload["customer"] = new Dictionary<string, object?> { ["id"] = customerId };
        }

        string setupTokenId;
        using (var setupDoc = await SendAsync(HttpMethod.Post, "v3/vault/setup-tokens", setupPayload, requestId, null, ct))
        {
            setupTokenId = Str(setupDoc.RootElement, "id") ?? throw ProtocolError("setup-token response missing id");
        }

        var tokenPayload = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?>
                {
                    ["id"] = setupTokenId,
                    ["type"] = "SETUP_TOKEN"
                }
            }
        };

        using var doc = await SendAsync(HttpMethod.Post, "v3/vault/payment-tokens", tokenPayload, $"{requestId}-confirm", null, ct);
        var root = doc.RootElement;

        var tokenId = Str(root, "id") ?? throw ProtocolError("payment-token response missing id");
        string? resolvedCustomerId = customerId;
        if (TryProp(root, "customer", out var customer))
        {
            resolvedCustomerId = Str(customer, "id") ?? customerId;
        }

        string brand = "CARD", last = string.Empty, expiry = card.Expiry, name = card.CardholderName ?? string.Empty;
        if (TryProp(root, "payment_source", out var ps) && TryProp(ps, "card", out var cardEl))
        {
            brand = Str(cardEl, "brand") ?? brand;
            last = Str(cardEl, "last_digits") ?? last;
            expiry = Str(cardEl, "expiry") ?? expiry;
            name = Str(cardEl, "name") ?? name;
        }

        return new VaultedCardResult(tokenId, resolvedCustomerId, brand, last, expiry, string.IsNullOrWhiteSpace(name) ? null : name);
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"v3/vault/payment-tokens/{paymentTokenId}", null, null, null, ct);
    }

    // ---------------------------------------------------------------- Reporting (reconciliation)

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset startInclusive,
        DateTimeOffset endInclusive, CancellationToken ct = default)
    {
        var results = new List<PayPalTransaction>();
        var start = FormatReportDate(startInclusive);
        var end = FormatReportDate(endInclusive);

        var page = 1;
        var totalPages = 1;
        do
        {
            var path = $"v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}" +
                       $"&fields=all&page_size=500&page={page}";
            using var doc = await SendAsync(HttpMethod.Get, path, null, null, null, ct);
            var root = doc.RootElement;

            totalPages = root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var pages) ? pages : 1;

            if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in details.EnumerateArray())
                {
                    if (!TryProp(entry, "transaction_info", out var info))
                    {
                        continue;
                    }

                    var txId = Str(info, "transaction_id");
                    if (string.IsNullOrWhiteSpace(txId))
                    {
                        continue;
                    }

                    results.Add(new PayPalTransaction(
                        txId!,
                        Str(info, "invoice_id"),
                        Str(info, "custom_field"),
                        Str(info, "transaction_status") ?? "UNKNOWN",
                        MoneyValue(info, "transaction_amount") ?? 0m,
                        CurrencyOf(info, "transaction_amount") ?? _settings.CurrencyCode,
                        MoneyValue(info, "fee_amount") ?? 0m,
                        ParseDate(Str(info, "transaction_initiation_date")) ?? startInclusive,
                        Str(info, "transaction_event_code")));
                }
            }

            page++;
        }
        while (page <= totalPages);

        return results;
    }

    // ---------------------------------------------------------------- HTTP plumbing

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, string? requestId,
        string? prefer, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        var token = await _tokenProvider.GetAccessTokenAsync(_http, ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (!string.IsNullOrEmpty(prefer))
        {
            request.Headers.TryAddWithoutValidation("Prefer", prefer);
        }

        if (body is not null)
        {
            // Card-bearing bodies are serialized here but never logged.
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var (debugId, issue, message) = ParseError(text, response.StatusCode);
            _logger.LogWarning("PayPal {Method} {Path} -> {Status} issue={Issue} debug_id={DebugId}",
                method, path, (int)response.StatusCode, issue, debugId);
            throw new PayPalApiException(message, (int)response.StatusCode, debugId, issue);
        }

        return string.IsNullOrWhiteSpace(text) ? JsonDocument.Parse("{}") : JsonDocument.Parse(text);
    }

    // ---------------------------------------------------------------- JSON helpers

    private static Dictionary<string, object?> MoneyNode(Money money) => new()
    {
        ["currency_code"] = money.CurrencyCode,
        ["value"] = money.Value.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static void AddBillingAddress(Dictionary<string, object?> cardNode, BillingAddress? address)
    {
        if (address is null)
        {
            return;
        }

        var node = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(address.AddressLine1)) node["address_line_1"] = address.AddressLine1;
        if (!string.IsNullOrWhiteSpace(address.AddressLine2)) node["address_line_2"] = address.AddressLine2;
        if (!string.IsNullOrWhiteSpace(address.AdminArea2)) node["admin_area_2"] = address.AdminArea2;
        if (!string.IsNullOrWhiteSpace(address.AdminArea1)) node["admin_area_1"] = address.AdminArea1;
        if (!string.IsNullOrWhiteSpace(address.PostalCode)) node["postal_code"] = address.PostalCode;
        if (!string.IsNullOrWhiteSpace(address.CountryCode)) node["country_code"] = address.CountryCode;

        if (node.Count > 0)
        {
            cardNode["billing_address"] = node;
        }
    }

    private static bool TryProp(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? Str(JsonElement element, string name) =>
        TryProp(element, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static decimal? MoneyValue(JsonElement parent, string name)
    {
        if (TryProp(parent, name, out var money) && TryProp(money, "value", out var value) && value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return null;
    }

    private static string? CurrencyOf(JsonElement parent, string name) =>
        TryProp(parent, name, out var money) ? Str(money, "currency_code") : null;

    private static bool TryGetAuthorization(JsonElement orderRoot, out JsonElement authorization)
    {
        authorization = default;
        if (!TryProp(orderRoot, "purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var unit in units.EnumerateArray())
        {
            if (TryProp(unit, "payments", out var payments) &&
                TryProp(payments, "authorizations", out var auths) &&
                auths.ValueKind == JsonValueKind.Array)
            {
                foreach (var auth in auths.EnumerateArray())
                {
                    authorization = auth;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasPayerActionLink(JsonElement root)
    {
        if (!TryProp(root, "links", out var links) || links.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return links.EnumerateArray().Any(l => string.Equals(Str(l, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase));
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    private static string FormatReportDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private (string? debugId, string? issue, string message) ParseError(string body, System.Net.HttpStatusCode status)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null, $"PayPal request failed (HTTP {(int)status}).");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var debugId = Str(root, "debug_id");
            var message = Str(root, "message") ?? Str(root, "error_description") ?? Str(root, "error");
            string? issue = null;

            if (TryProp(root, "details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    issue = Str(detail, "issue");
                    var description = Str(detail, "description");
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        message = string.IsNullOrWhiteSpace(message) ? description : $"{message} ({description})";
                    }

                    break;
                }
            }

            return (debugId, issue, message ?? $"PayPal request failed (HTTP {(int)status}).");
        }
        catch (JsonException)
        {
            return (null, null, $"PayPal request failed (HTTP {(int)status}).");
        }
    }

    private static PayPalApiException ProtocolError(string what) =>
        new($"Unexpected PayPal response: {what}.", 502);
}

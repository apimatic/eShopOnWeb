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
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Talks to PayPal's REST APIs: Orders v2 (authorize), Payments v2 (capture,
/// reauthorize, void, refund), Vault v3 (save/list/delete cards) and Transaction
/// Search v1 (reconciliation). All amounts use the configured currency.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private readonly HttpClient _http;
    private readonly IPayPalAccessTokenProvider _tokenProvider;
    private readonly PayPalSettings _settings;

    public PayPalPaymentGateway(HttpClient http, IPayPalAccessTokenProvider tokenProvider, IOptions<PayPalSettings> settings)
    {
        _http = http;
        _tokenProvider = tokenProvider;
        _settings = settings.Value;
    }

    public string ConfiguredCurrency => _settings.Currency;

    // ---------------------------------------------------------------- Orders v2

    public async Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, CardPaymentDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = BuildOrderBody(amount, new JsonObject { ["card"] = BuildCardNode(card) });
        var root = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, idempotencyKey, cancellationToken);
        return ParseAuthorization(root);
    }

    public async Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var card = new JsonObject { ["vault_id"] = vaultId };
        var body = BuildOrderBody(amount, new JsonObject { ["card"] = card });
        var root = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, idempotencyKey, cancellationToken);
        return ParseAuthorization(root);
    }

    private JsonObject BuildOrderBody(decimal amount, JsonObject paymentSource)
    {
        return new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject { ["amount"] = BuildMoney(amount) }
            },
            ["payment_source"] = paymentSource
        };
    }

    private AuthorizationResult ParseAuthorization(JsonElement root)
    {
        var status = GetString(root, "status") ?? "";
        // A raw card should authorize inline; a challenge (browser approval) is not supported here.
        if (status.Equals("PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) || HasLink(root, "payer-action"))
        {
            throw new PaymentGatewayException(
                "PayPal requires shopper approval in a browser for this card (3-D Secure challenge). This is not supported.",
                (int)HttpStatusCode.UnprocessableEntity, GetString(root, "debug_id"), new[] { "PAYER_ACTION_REQUIRED" });
        }

        var payPalOrderId = GetString(root, "id") ?? throw Malformed("order id");

        if (!root.TryGetProperty("purchase_units", out var pus) || pus.GetArrayLength() == 0)
        {
            throw Malformed("purchase_units");
        }
        var pu = pus[0];
        if (!pu.TryGetProperty("payments", out var payments) ||
            !payments.TryGetProperty("authorizations", out var auths) || auths.GetArrayLength() == 0)
        {
            throw Malformed("authorizations");
        }
        var auth = auths[0];

        var authId = GetString(auth, "id") ?? throw Malformed("authorization id");
        var authStatus = GetString(auth, "status") ?? "CREATED";
        var expiresAt = GetDate(auth, "expiration_time");

        var (brand, last4) = ParseCardDescriptor(root);

        return new AuthorizationResult(payPalOrderId, authId, authStatus, expiresAt, _settings.Currency, brand, last4);
    }

    private (string brand, string last4) ParseCardDescriptor(JsonElement root)
    {
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var card))
        {
            var brand = GetString(card, "brand") ?? "CARD";
            var last4 = GetString(card, "last_digits") ?? "****";
            return (brand, last4);
        }
        return ("CARD", "****");
    }

    // -------------------------------------------------------------- Payments v2

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = BuildMoney(amount),
            ["final_capture"] = true
        };
        var root = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", body, idempotencyKey, cancellationToken);

        var captureId = GetString(root, "id") ?? throw Malformed("capture id");
        var status = GetString(root, "status") ?? "COMPLETED";

        var breakdown = TryGetBreakdown(root);
        if (breakdown is null)
        {
            // PayPal doesn't always include the fee breakdown on the capture response;
            // read it back from the capture resource so we can report fee and net.
            var captureRoot = await SendAsync(HttpMethod.Get,
                $"/v2/payments/captures/{captureId}", null, null, cancellationToken);
            breakdown = TryGetBreakdown(captureRoot);
        }

        var gross = breakdown?.Gross ?? amount;
        var fee = breakdown?.Fee ?? 0m;
        var net = breakdown?.Net ?? (gross - fee);

        return new CaptureResult(captureId, status, gross, fee, net, _settings.Currency);
    }

    public async Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["amount"] = BuildMoney(amount) };
        var root = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, idempotencyKey, cancellationToken);

        var newId = GetString(root, "id") ?? throw Malformed("reauthorization id");
        var status = GetString(root, "status") ?? "CREATED";
        var expiresAt = GetDate(root, "expiration_time");
        return new ReauthorizationResult(newId, status, expiresAt);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, null, cancellationToken);
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["amount"] = BuildMoney(amount) };
        var root = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund", body, idempotencyKey, cancellationToken);

        var refundId = GetString(root, "id") ?? throw Malformed("refund id");
        var status = GetString(root, "status") ?? "COMPLETED";
        var refundedAmount = amount;
        if (root.TryGetProperty("amount", out var amt) && amt.TryGetProperty("value", out var val))
        {
            refundedAmount = ParseDecimal(val.GetString());
        }
        return new RefundResult(refundId, status, refundedAmount, _settings.Currency);
    }

    // ----------------------------------------------------------------- Vault v3

    public async Task<VaultedCardResult> VaultCardAsync(CardPaymentDetails card, string? customerId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Step 1: tokenize the raw card into a setup token.
        var setupBody = new JsonObject
        {
            ["payment_source"] = new JsonObject { ["card"] = BuildCardNode(card) }
        };
        var setupRoot = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody,
            $"{idempotencyKey}-setup", cancellationToken);
        var setupTokenId = GetString(setupRoot, "id") ?? throw Malformed("setup token id");

        // Step 2: exchange the setup token for a permanent payment (vault) token.
        var tokenBody = new JsonObject
        {
            ["payment_source"] = new JsonObject
            {
                ["token"] = new JsonObject { ["id"] = setupTokenId, ["type"] = "SETUP_TOKEN" }
            }
        };
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            tokenBody["customer"] = new JsonObject { ["id"] = customerId };
        }

        var tokenRoot = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenBody,
            $"{idempotencyKey}-token", cancellationToken);

        var vaultId = GetString(tokenRoot, "id") ?? throw Malformed("vault token id");
        string? resolvedCustomer = null;
        if (tokenRoot.TryGetProperty("customer", out var cust))
        {
            resolvedCustomer = GetString(cust, "id");
        }

        var brand = "CARD";
        var last4 = card.Last4();
        var expiry = card.ExpiryYyyyMm();
        if (tokenRoot.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            brand = GetString(cardEl, "brand") ?? brand;
            last4 = GetString(cardEl, "last_digits") ?? last4;
            expiry = GetString(cardEl, "expiry") ?? expiry;
        }

        return new VaultedCardResult(vaultId, resolvedCustomer, brand, last4, expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, cancellationToken);
    }

    // ------------------------------------------------------ Transaction Search v1

    public async Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<GatewayTransaction>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // PayPal caps a single query at 31 days, so walk the range in windows and page each.
        var cursor = from;
        while (cursor < to)
        {
            var windowEnd = cursor.AddDays(31);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await CollectWindowAsync(cursor, windowEnd, results, seen, cancellationToken);
            cursor = windowEnd;
        }

        return results;
    }

    private async Task CollectWindowAsync(DateTimeOffset start, DateTimeOffset end,
        List<GatewayTransaction> results, HashSet<string> seen, CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        var page = 1;
        var totalPages = 1;

        do
        {
            var query = $"/v1/reporting/transactions?start_date={Iso(start)}&end_date={Iso(end)}" +
                        $"&fields=all&page_size={pageSize}&page={page}";
            var root = await SendAsync(HttpMethod.Get, query, null, null, cancellationToken);

            totalPages = root.TryGetProperty("total_pages", out var tp) ? tp.GetInt32() : 0;

            if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (!detail.TryGetProperty("transaction_info", out var info))
                    {
                        continue;
                    }

                    var txId = GetString(info, "transaction_id");
                    if (string.IsNullOrEmpty(txId) || !seen.Add(txId!))
                    {
                        continue;
                    }

                    var (amount, currency) = ParseMoney(info, "transaction_amount");
                    var (fee, _) = ParseMoney(info, "fee_amount");
                    var hasFee = info.TryGetProperty("fee_amount", out _);

                    results.Add(new GatewayTransaction(
                        TransactionId: txId!,
                        Status: GetString(info, "transaction_status") ?? "",
                        Amount: amount,
                        Currency: string.IsNullOrEmpty(currency) ? _settings.Currency : currency,
                        Fee: hasFee ? fee : null,
                        Date: GetDate(info, "transaction_initiation_date") ?? start,
                        EventCode: GetString(info, "transaction_event_code"),
                        InvoiceId: GetString(info, "invoice_id")));
                }
            }

            page++;
        }
        while (page <= totalPages);
    }

    // --------------------------------------------------------------- HTTP plumbing

    private async Task<JsonElement> SendAsync(HttpMethod method, string requestUri, JsonNode? body,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ParseError((int)response.StatusCode, content);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        using var doc = JsonDocument.Parse(content);
        return doc.RootElement.Clone();
    }

    private static PaymentGatewayException ParseError(int statusCode, string content)
    {
        string? debugId = null;
        string message = $"PayPal request failed (HTTP {statusCode}).";
        var issues = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            debugId = root.TryGetProperty("debug_id", out var d) ? d.GetString() : null;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            var msg = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            if (name is not null)
            {
                issues.Add(name);
            }
            if (root.TryGetProperty("details", out var det) && det.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in det.EnumerateArray())
                {
                    if (item.TryGetProperty("issue", out var iss) && iss.GetString() is { } issueCode)
                    {
                        issues.Add(issueCode);
                    }
                }
            }
            if (!string.IsNullOrEmpty(msg))
            {
                message = $"PayPal request failed (HTTP {statusCode}): {msg}";
            }
        }
        catch
        {
            // Non-JSON error body; keep the generic message.
        }

        return new PaymentGatewayException(message, statusCode, debugId, issues);
    }

    // ------------------------------------------------------------------- Helpers

    private JsonObject BuildMoney(decimal amount) => new()
    {
        ["currency_code"] = _settings.Currency,
        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static JsonObject BuildCardNode(CardPaymentDetails card)
    {
        var billing = new JsonObject
        {
            ["address_line_1"] = card.BillingAddressLine1,
            ["admin_area_2"] = card.BillingCity,
            ["postal_code"] = card.BillingPostalCode,
            ["country_code"] = card.BillingCountryCode
        };
        if (!string.IsNullOrWhiteSpace(card.BillingAddressLine2))
        {
            billing["address_line_2"] = card.BillingAddressLine2;
        }
        if (!string.IsNullOrWhiteSpace(card.BillingState))
        {
            billing["admin_area_1"] = card.BillingState;
        }

        return new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.ExpiryYyyyMm(),
            ["security_code"] = card.SecurityCode,
            ["name"] = card.CardholderName,
            ["billing_address"] = billing
        };
    }

    private sealed record Breakdown(decimal Gross, decimal Fee, decimal Net);

    private static Breakdown? TryGetBreakdown(JsonElement root)
    {
        if (!root.TryGetProperty("seller_receivable_breakdown", out var b) || b.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var gross = ReadMoneyValue(b, "gross_amount");
        var fee = ReadMoneyValue(b, "paypal_fee");
        var net = ReadMoneyValue(b, "net_amount");
        if (gross is null)
        {
            return null;
        }
        return new Breakdown(gross.Value, fee ?? 0m, net ?? (gross.Value - (fee ?? 0m)));
    }

    private static decimal? ReadMoneyValue(JsonElement parent, string name)
    {
        if (parent.TryGetProperty(name, out var money) && money.TryGetProperty("value", out var val))
        {
            return ParseDecimal(val.GetString());
        }
        return null;
    }

    private static (decimal amount, string currency) ParseMoney(JsonElement parent, string name)
    {
        if (parent.TryGetProperty(name, out var money))
        {
            var value = money.TryGetProperty("value", out var v) ? ParseDecimal(v.GetString()) : 0m;
            var currency = money.TryGetProperty("currency_code", out var c) ? c.GetString() ?? "" : "";
            return (value, currency);
        }
        return (0m, "");
    }

    private static string? GetString(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static DateTimeOffset? GetDate(JsonElement el, string name)
    {
        var s = GetString(el, name);
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
            ? dt
            : null;
    }

    private static bool HasLink(JsonElement root, string rel)
    {
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                if (GetString(link, "rel")?.Equals(rel, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static decimal ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    private static string Iso(DateTimeOffset value) =>
        Uri.EscapeDataString(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

    private static PaymentGatewayException Malformed(string what) =>
        new($"PayPal response was missing an expected field: {what}.", 500, null, Array.Empty<string>());
}

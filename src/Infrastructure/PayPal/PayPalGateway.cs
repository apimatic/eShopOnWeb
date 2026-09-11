using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The PayPal adapter. Every request body and response reading here is built to PayPal's
/// OpenAPI specification: Checkout Orders v2 (create/authorize), Payments v2
/// (capture/void/reauthorize/refund), Vault v3 (save/delete card) and Transaction Search v1
/// (reconciliation).
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private readonly PayPalHttpClient _http;
    private readonly ILogger<PayPalGateway> _logger;

    // ISO-4217 currencies with no minor unit; everything else uses two decimal places.
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "HUF", "TWD", "CLP", "KRW", "VND"
    };

    public PayPalGateway(PayPalHttpClient http, ILogger<PayPalGateway> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<AuthorizeResult> AuthorizeAsync(AuthorizeInstruction instruction, CancellationToken ct = default)
    {
        var purchaseUnit = new JsonObject
        {
            ["custom_id"] = instruction.OrderReference,
            ["amount"] = Amount(instruction.CurrencyCode, instruction.Amount)
        };

        var body = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray(purchaseUnit),
            ["payment_source"] = new JsonObject { ["card"] = BuildCardNode(instruction.Source) }
        };

        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = instruction.PayPalRequestId,
            ["Prefer"] = "return=representation"
        };

        JsonDocument? doc;
        try
        {
            doc = await _http.SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, headers, ct);
        }
        catch (PayPalApiException ex) when (IsCardProcessingFailure(ex))
        {
            // A declined card is an expected outcome, not a transport error.
            return new AuthorizeResult(AuthorizeStatus.Failed, null, null, null, null, ex.Issue ?? ex.Description ?? ex.Name);
        }

        return await InterpretOrderAsync(doc, ct);
    }

    private async Task<AuthorizeResult> InterpretOrderAsync(JsonDocument? doc, CancellationToken ct)
    {
        if (doc is null)
            return new AuthorizeResult(AuthorizeStatus.Failed, null, null, null, null, "PayPal returned an empty response.");

        var root = doc.RootElement;
        var payPalOrderId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        var status = root.TryGetProperty("status", out var stEl) ? stEl.GetString() : null;

        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            return new AuthorizeResult(AuthorizeStatus.ChallengeRequired, payPalOrderId, null, null, null, "Payer action required");

        var auth = FindFirstAuthorization(root);

        // The order was created but the funds still need an explicit authorize step.
        if (auth is null && string.Equals(status, "APPROVED", StringComparison.OrdinalIgnoreCase) && payPalOrderId is not null)
        {
            var headers = new Dictionary<string, string> { ["Prefer"] = "return=representation" };
            var authDoc = await _http.SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize", new JsonObject(), headers, ct);
            if (authDoc is not null)
            {
                root = authDoc.RootElement;
                status = root.TryGetProperty("status", out var st2) ? st2.GetString() : status;
                auth = FindFirstAuthorization(root);
            }
            authDoc?.Dispose();
        }

        if (auth is null)
            return new AuthorizeResult(AuthorizeStatus.Failed, payPalOrderId, null, null, null, $"No authorization was created (order status {status}).");

        var (authId, authStatus, expiresAt) = auth.Value;
        if (string.Equals(authStatus, "DENIED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(authStatus, "VOIDED", StringComparison.OrdinalIgnoreCase))
            return new AuthorizeResult(AuthorizeStatus.Failed, payPalOrderId, authId, authStatus, expiresAt, $"Authorization {authStatus}.");

        return new AuthorizeResult(AuthorizeStatus.Authorized, payPalOrderId, authId, authStatus, expiresAt, null);
    }

    public async Task<AuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        var doc = await _http.SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, ct)
            ?? throw new PaymentGatewayEmptyResponse();
        var root = doc.RootElement;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "UNKNOWN" : "UNKNOWN";
        var expiresAt = ReadDate(root, "expiration_time");
        var info = new AuthorizationInfo(authorizationId, status, expiresAt);
        doc.Dispose();
        return info;
    }

    public async Task<AuthorizationInfo> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string payPalRequestId, CancellationToken ct = default)
    {
        var body = new JsonObject { ["amount"] = Amount(currencyCode, amount) };
        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = payPalRequestId,
            ["Prefer"] = "return=representation"
        };
        var doc = await _http.SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, headers, ct)
            ?? throw new PaymentGatewayEmptyResponse();
        var root = doc.RootElement;
        var newId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? authorizationId : authorizationId;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "CREATED" : "CREATED";
        var expiresAt = ReadDate(root, "expiration_time");
        doc.Dispose();
        return new AuthorizationInfo(newId, status, expiresAt);
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currencyCode, string invoiceId, string payPalRequestId, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["amount"] = Amount(currencyCode, amount),
            ["invoice_id"] = invoiceId,
            ["final_capture"] = true
        };
        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = payPalRequestId,
            ["Prefer"] = "return=representation"
        };
        var doc = await _http.SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, headers, ct)
            ?? throw new PaymentGatewayEmptyResponse();
        var root = doc.RootElement;

        var captureId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "COMPLETED" : "COMPLETED";
        var gross = ReadMoney(root, "amount") ?? amount;
        decimal? fee = null, net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            gross = ReadMoney(breakdown, "gross_amount") ?? gross;
            fee = ReadMoney(breakdown, "paypal_fee");
            net = ReadMoney(breakdown, "net_amount");
        }
        doc.Dispose();
        return new CaptureResult(captureId, status, gross, fee, net, currencyCode);
    }

    public async Task VoidAsync(string authorizationId, string payPalRequestId, CancellationToken ct = default)
    {
        var headers = new Dictionary<string, string> { ["PayPal-Request-Id"] = payPalRequestId };
        var doc = await _http.SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, headers, ct);
        doc?.Dispose();
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string invoiceId, string payPalRequestId, CancellationToken ct = default)
    {
        var body = new JsonObject();
        if (!string.IsNullOrEmpty(invoiceId))
            body["invoice_id"] = invoiceId;
        if (amount is not null)
            body["amount"] = Amount(currencyCode, amount.Value);

        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = payPalRequestId,
            ["Prefer"] = "return=representation"
        };
        var doc = await _http.SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, headers, ct)
            ?? throw new PaymentGatewayEmptyResponse();
        var root = doc.RootElement;
        var refundId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "COMPLETED" : "COMPLETED";
        var refundedAmount = ReadMoney(root, "amount") ?? amount ?? 0m;
        doc.Dispose();
        return new RefundResult(refundId, status, refundedAmount, currencyCode);
    }

    public async Task<VaultCardResult> VaultCardAsync(string customerId, CardDetails card, string payPalRequestId, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["customer"] = new JsonObject { ["id"] = customerId },
            ["payment_source"] = new JsonObject { ["card"] = BuildRawCardNode(card) }
        };
        var headers = new Dictionary<string, string> { ["PayPal-Request-Id"] = payPalRequestId };

        var doc = await _http.SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, headers, ct)
            ?? throw new PaymentGatewayEmptyResponse();
        var root = doc.RootElement;

        var vaultId = root.GetProperty("id").GetString()!;
        var returnedCustomerId = customerId;
        if (root.TryGetProperty("customer", out var cust) && cust.TryGetProperty("id", out var cid))
            returnedCustomerId = cid.GetString() ?? customerId;

        string? brand = null, lastDigits = null, expiry = null, name = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            brand = cardEl.TryGetProperty("brand", out var b) ? b.GetString() : null;
            lastDigits = cardEl.TryGetProperty("last_digits", out var l) ? l.GetString() : null;
            expiry = cardEl.TryGetProperty("expiry", out var e) ? e.GetString() : null;
            name = cardEl.TryGetProperty("name", out var nm) ? nm.GetString() : null;
        }
        doc.Dispose();
        return new VaultCardResult(vaultId, returnedCustomerId, brand, lastDigits, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        var doc = await _http.SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, ct);
        doc?.Dispose();
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<PayPalTransaction>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const int pageSize = 100;

        // PayPal's Transaction Search allows at most a 31-day range per request, so walk the
        // whole requested range in sub-windows and page through each so nothing is missed.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to) windowEnd = to;

            var start = Uri.EscapeDataString(FormatDate(windowStart));
            var end = Uri.EscapeDataString(FormatDate(windowEnd));

            int page = 1, totalPages = 1;
            do
            {
                var path = $"/v1/reporting/transactions?start_date={start}&end_date={end}&fields=all&page_size={pageSize}&page={page}";
                var doc = await _http.SendAsync(HttpMethod.Get, path, null, null, ct);
                if (doc is null) break;
                var root = doc.RootElement;

                totalPages = root.TryGetProperty("total_pages", out var tp) ? tp.GetInt32() : page;

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d in details.EnumerateArray())
                    {
                        if (!d.TryGetProperty("transaction_info", out var info)) continue;
                        var txn = ReadTransaction(info);
                        // De-duplicate across window boundaries.
                        if (string.IsNullOrEmpty(txn.TransactionId) || seen.Add(txn.TransactionId))
                            results.Add(txn);
                    }
                }
                doc.Dispose();
                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd == to ? to : windowEnd;
            if (windowEnd == to) break;
        }

        return results;
    }

    // ----- request builders -----

    private static JsonObject BuildCardNode(PaymentSourceInstruction source)
    {
        if (source.VaultId is not null)
            return new JsonObject { ["vault_id"] = source.VaultId };
        return BuildRawCardNode(source.Card!);
    }

    private static JsonObject BuildRawCardNode(CardDetails card)
    {
        var node = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry
        };
        if (!string.IsNullOrEmpty(card.SecurityCode)) node["security_code"] = card.SecurityCode;
        if (!string.IsNullOrEmpty(card.Name)) node["name"] = card.Name;
        if (card.Billing is not null)
        {
            var addr = new JsonObject { ["country_code"] = card.Billing.CountryCode };
            if (!string.IsNullOrEmpty(card.Billing.AddressLine1)) addr["address_line_1"] = card.Billing.AddressLine1;
            if (!string.IsNullOrEmpty(card.Billing.AddressLine2)) addr["address_line_2"] = card.Billing.AddressLine2;
            if (!string.IsNullOrEmpty(card.Billing.AdminArea2)) addr["admin_area_2"] = card.Billing.AdminArea2;
            if (!string.IsNullOrEmpty(card.Billing.AdminArea1)) addr["admin_area_1"] = card.Billing.AdminArea1;
            if (!string.IsNullOrEmpty(card.Billing.PostalCode)) addr["postal_code"] = card.Billing.PostalCode;
            node["billing_address"] = addr;
        }
        return node;
    }

    private JsonObject Amount(string currencyCode, decimal value) => new()
    {
        ["currency_code"] = currencyCode,
        ["value"] = FormatMoney(currencyCode, value)
    };

    // ----- response readers -----

    private static (string authId, string? status, DateTimeOffset? expiresAt)? FindFirstAuthorization(JsonElement root)
    {
        if (!root.TryGetProperty("purchase_units", out var pus) || pus.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var pu in pus.EnumerateArray())
        {
            if (!pu.TryGetProperty("payments", out var payments)) continue;
            if (!payments.TryGetProperty("authorizations", out var auths) || auths.ValueKind != JsonValueKind.Array) continue;
            foreach (var a in auths.EnumerateArray())
            {
                var id = a.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (id is null) continue;
                var status = a.TryGetProperty("status", out var st) ? st.GetString() : null;
                var expires = ReadDate(a, "expiration_time");
                return (id, status, expires);
            }
        }
        return null;
    }

    private static PayPalTransaction ReadTransaction(JsonElement info)
    {
        string? Get(string name) => info.TryGetProperty(name, out var el) ? el.GetString() : null;

        var txnId = Get("transaction_id") ?? string.Empty;
        var referenceId = Get("paypal_reference_id");
        var eventCode = Get("transaction_event_code");
        var status = Get("transaction_status");
        var initiation = ReadDate(info, "transaction_initiation_date");
        var amount = ReadMoney(info, "transaction_amount");
        var fee = ReadMoney(info, "fee_amount");
        var currency = info.TryGetProperty("transaction_amount", out var amtEl) && amtEl.TryGetProperty("currency_code", out var cc)
            ? cc.GetString() : null;

        return new PayPalTransaction(txnId, referenceId, eventCode, status, amount, currency, fee, initiation);
    }

    private static decimal? ReadMoney(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var money)) return null;
        if (!money.TryGetProperty("value", out var val)) return null;
        var raw = val.GetString();
        if (raw is null) return null;
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static DateTimeOffset? ReadDate(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var el)) return null;
        var raw = el.GetString();
        if (raw is null) return null;
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
    }

    private static string FormatMoney(string currencyCode, decimal value)
    {
        var decimals = ZeroDecimalCurrencies.Contains(currencyCode) ? 0 : 2;
        return Math.Round(value, decimals, MidpointRounding.AwayFromZero)
            .ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    // PayPal signals a declined / unprocessable card with 4xx business errors rather than
    // transport failures; these are surfaced as a failed authorization, not an exception.
    private static bool IsCardProcessingFailure(PayPalApiException ex) =>
        ex.StatusCode is HttpStatusCode.UnprocessableEntity
            or HttpStatusCode.PaymentRequired;

    private sealed class PaymentGatewayEmptyResponse : PaymentException
    {
        public PaymentGatewayEmptyResponse() : base("PayPal returned an unexpected empty response.") { }
    }
}

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
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Hand-written typed client over the PayPal REST APIs, built to the OpenAPI specs in
/// <c>api-specs/paypal</c>: Checkout Orders v2, Payments v2, Vault Payment Tokens v3 and
/// Transaction Search v1. No third-party PayPal SDK is used. Request/response shapes, paths and
/// the error model all follow the specs; snake_case JSON keys are written explicitly.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private readonly HttpClient _httpClient;
    private readonly PayPalTokenProvider _tokenProvider;
    private readonly PayPalSettings _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public PayPalClient(HttpClient httpClient, PayPalTokenProvider tokenProvider, PayPalSettings settings)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _settings = settings;
    }

    // ---------- Checkout Orders v2: authorize a hold ----------

    public Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency,
        string invoiceId, PayPalCardInput card, string requestId, CancellationToken ct = default)
        => AuthorizeAsync(amount, currency, invoiceId, BuildCardPayload(card), requestId, ct);

    public Task<PayPalAuthorizationResult> AuthorizeWithVaultAsync(decimal amount, string currency,
        string invoiceId, string vaultId, string requestId, CancellationToken ct = default)
        => AuthorizeAsync(amount, currency, invoiceId,
            new Dictionary<string, object?> { ["vault_id"] = vaultId }, requestId, ct);

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(decimal amount, string currency,
        string invoiceId, Dictionary<string, object?> cardPayload, string requestId, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["invoice_id"] = invoiceId,
                    ["amount"] = Money(amount, currency)
                }
            },
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = cardPayload }
        };

        using var root = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId, ct);
        var el = root.RootElement;

        var payPalOrderId = GetString(el, "id")!;
        var orderStatus = GetString(el, "status") ?? "";

        string? authId = null, authStatus = null, cardBrand = null, cardLast = null;
        if (el.TryGetProperty("purchase_units", out var pus) && pus.GetArrayLength() > 0)
        {
            var pu = pus[0];
            if (pu.TryGetProperty("payments", out var payments) &&
                payments.TryGetProperty("authorizations", out var auths) && auths.GetArrayLength() > 0)
            {
                var auth = auths[0];
                authId = GetString(auth, "id");
                authStatus = GetString(auth, "status");
            }
        }
        if (el.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var respCard))
        {
            cardBrand = GetString(respCard, "brand");
            cardLast = GetString(respCard, "last_digits");
        }

        var requiresPayerAction = string.Equals(orderStatus, "PAYER_ACTION_REQUIRED",
            StringComparison.OrdinalIgnoreCase);
        return new PayPalAuthorizationResult(payPalOrderId, orderStatus, authId, authStatus,
            cardBrand, cardLast, requiresPayerAction);
    }

    // ---------- Payments v2: capture / reauthorize / void / refund ----------

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId,
        string requestId, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["final_capture"] = true };
        using var root = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", body, requestId, ct);
        return ParseCapture(root.RootElement);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken ct = default)
    {
        using var root = await SendJsonAsync(HttpMethod.Get, $"/v2/payments/captures/{captureId}", null, null, ct);
        return ParseCapture(root.RootElement);
    }

    public async Task<string?> GetAuthorizationStatusAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            using var root = await SendJsonAsync(HttpMethod.Get,
                $"/v2/payments/authorizations/{authorizationId}", null, null, ct);
            return GetString(root.RootElement, "status");
        }
        catch (PayPalApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<PayPalReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["amount"] = Money(amount, currency) };
        using var root = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, requestId, ct);
        var el = root.RootElement;
        return new PayPalReauthorizeResult(GetString(el, "id")!, GetString(el, "status") ?? "CREATED");
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken ct = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void", null, requestId, ct);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount,
        string currency, string requestId, CancellationToken ct = default)
    {
        Dictionary<string, object?>? body = amount is decimal value
            ? new Dictionary<string, object?> { ["amount"] = Money(value, currency) }
            : null; // omitted amount => full refund of the remaining balance
        using var root = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund", body, requestId, ct);
        var el = root.RootElement;
        var refundedAmount = 0m;
        if (el.TryGetProperty("amount", out var amt))
        {
            refundedAmount = ParseDecimal(GetString(amt, "value"));
        }
        return new PayPalRefundResult(GetString(el, "id")!, GetString(el, "status") ?? "", refundedAmount);
    }

    // ---------- Vault Payment Tokens v3 ----------

    public async Task<PayPalVaultCardResult> VaultCardAsync(PayPalCardInput card, string? customerId,
        string requestId, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = BuildCardPayload(card) }
        };
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            body["customer"] = new Dictionary<string, object?> { ["id"] = customerId };
        }

        using var root = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, requestId, ct);
        var el = root.RootElement;
        var vaultId = GetString(el, "id")!;
        string? returnedCustomer = null;
        if (el.TryGetProperty("customer", out var cust))
        {
            returnedCustomer = GetString(cust, "id");
        }

        string brand = "", last = "", expiry = "";
        string? name = null;
        if (el.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var c))
        {
            brand = GetString(c, "brand") ?? "";
            last = GetString(c, "last_digits") ?? "";
            expiry = GetString(c, "expiry") ?? "";
            name = GetString(c, "name");
        }
        return new PayPalVaultCardResult(vaultId, returnedCustomer, brand, last, expiry, name);
    }

    public async Task DeleteVaultTokenAsync(string vaultId, CancellationToken ct = default)
    {
        using var _ = await SendJsonAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, ct);
    }

    // ---------- Transaction Search v1: reconciliation ----------

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<PayPalTransactionRecord>();
        const int pageSize = 500; // spec maximum
        var page = 1;
        var totalPages = 1;

        do
        {
            var start = Uri.EscapeDataString(FormatDate(from));
            var end = Uri.EscapeDataString(FormatDate(to));
            var path = $"/v1/reporting/transactions?start_date={start}&end_date={end}" +
                       $"&fields=transaction_info&page_size={pageSize}&page={page}";

            using var root = await SendJsonAsync(HttpMethod.Get, path, null, null, ct);
            var el = root.RootElement;

            if (el.TryGetProperty("total_pages", out var tp) && tp.ValueKind == JsonValueKind.Number)
            {
                totalPages = tp.GetInt32();
            }

            if (el.TryGetProperty("transaction_details", out var details) &&
                details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.TryGetProperty("transaction_info", out var info))
                    {
                        results.Add(ParseTransaction(info));
                    }
                }
            }

            page++;
        }
        while (page <= totalPages);

        return results;
    }

    // ---------- shared helpers ----------

    private static Dictionary<string, object?> BuildCardPayload(PayPalCardInput card)
    {
        var payload = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["name"] = card.Name
        };
        if (card.BillingAddress is { } addr)
        {
            payload["billing_address"] = new Dictionary<string, object?>
            {
                ["address_line_1"] = addr.AddressLine1,
                ["address_line_2"] = addr.AddressLine2,
                ["admin_area_2"] = addr.AdminArea2,
                ["admin_area_1"] = addr.AdminArea1,
                ["postal_code"] = addr.PostalCode,
                ["country_code"] = addr.CountryCode
            };
        }
        return payload;
    }

    private static PayPalCaptureResult ParseCapture(JsonElement el)
    {
        var captureId = GetString(el, "id")!;
        var status = GetString(el, "status") ?? "";
        var amount = 0m;
        if (el.TryGetProperty("amount", out var amt))
        {
            amount = ParseDecimal(GetString(amt, "value"));
        }
        decimal? fee = null, net = null;
        if (el.TryGetProperty("seller_receivable_breakdown", out var b))
        {
            if (b.TryGetProperty("paypal_fee", out var f)) fee = ParseDecimal(GetString(f, "value"));
            if (b.TryGetProperty("net_amount", out var n)) net = ParseDecimal(GetString(n, "value"));
            if (b.TryGetProperty("gross_amount", out var g) && amount == 0m)
                amount = ParseDecimal(GetString(g, "value"));
        }
        return new PayPalCaptureResult(captureId, status, amount, fee, net);
    }

    private static PayPalTransactionRecord ParseTransaction(JsonElement info)
    {
        decimal? amount = null;
        string? currency = null;
        if (info.TryGetProperty("transaction_amount", out var amt))
        {
            amount = ParseNullableDecimal(GetString(amt, "value"));
            currency = GetString(amt, "currency_code");
        }
        decimal? fee = null;
        if (info.TryGetProperty("fee_amount", out var feeEl))
        {
            fee = ParseNullableDecimal(GetString(feeEl, "value"));
        }
        DateTimeOffset? initDate = null;
        var initRaw = GetString(info, "transaction_initiation_date");
        if (initRaw is not null && DateTimeOffset.TryParse(initRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed))
        {
            initDate = parsed;
        }

        return new PayPalTransactionRecord(
            GetString(info, "transaction_id") ?? "",
            GetString(info, "invoice_id"),
            GetString(info, "custom_field"),
            GetString(info, "transaction_status"),
            GetString(info, "transaction_event_code"),
            amount, currency, fee, initDate);
    }

    private static Dictionary<string, object?> Money(decimal amount, string currency) => new()
    {
        ["currency_code"] = currency,
        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static string? GetString(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) &&
        v.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? v.ToString()
            : null;

    private static decimal ParseDecimal(string? value) => ParseNullableDecimal(value) ?? 0m;

    private static decimal? ParseNullableDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    /// <summary>
    /// Sends a request with a bearer token and returns the parsed JSON document (empty document
    /// for no-content responses). Translates any PayPal error into <see cref="PayPalApiException"/>.
    /// </summary>
    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path,
        Dictionary<string, object?>? body, string? requestId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        var token = await _tokenProvider.GetAccessTokenAsync(ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (requestId is not null)
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildError((int)response.StatusCode, content);
        }

        return string.IsNullOrWhiteSpace(content)
            ? JsonDocument.Parse("{}")
            : JsonDocument.Parse(content);
    }

    private static PayPalApiException BuildError(int statusCode, string content)
    {
        string? name = null, message = null, debugId = null;
        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                using var doc = JsonDocument.Parse(content);
                var el = doc.RootElement;
                name = GetString(el, "name") ?? GetString(el, "error");
                message = GetString(el, "message") ?? GetString(el, "error_description");
                debugId = GetString(el, "debug_id");
                if (el.TryGetProperty("details", out var details) &&
                    details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0)
                {
                    var first = details[0];
                    var issue = GetString(first, "issue");
                    var description = GetString(first, "description");
                    if (issue is not null)
                    {
                        name = issue; // the issue code is the actionable identifier
                        message = description ?? message ?? issue;
                    }
                }
            }
            catch (JsonException)
            {
                message = content;
            }
        }

        return new PayPalApiException(statusCode, name,
            message ?? $"PayPal request failed with status {statusCode}.", debugId);
    }
}

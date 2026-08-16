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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Paypal;

namespace Microsoft.eShopWeb.Infrastructure.Paypal;

/// <summary>
/// Talks to PayPal's REST APIs over HTTPS: OAuth token (v1), Orders v2, Payments v2 (authorizations,
/// captures, refunds), Vault v3 (saved cards) and Reporting v1 (transaction search). Card numbers are
/// forwarded to PayPal and never persisted or logged here.
/// </summary>
public class PayPalClient : IPayPalPaymentGateway
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly PayPalTokenProvider _tokenProvider;
    private readonly IAppLogger<PayPalClient> _logger;

    // Transaction Search allows at most a 31-day window per request; we chunk larger ranges.
    private static readonly TimeSpan MaxReportingWindow = TimeSpan.FromDays(31);
    private const int ReportingPageSize = 500;

    public PayPalClient(IHttpClientFactory httpFactory, PayPalTokenProvider tokenProvider,
        IAppLogger<PayPalClient> logger)
    {
        _httpFactory = httpFactory;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    // -------------------------------------------------------------------------------------------
    // Authorize (Orders v2): create an order with intent=AUTHORIZE and the card/vaulted source,
    // then make sure a hold exists (authorizing explicitly if the order still needs it).
    // -------------------------------------------------------------------------------------------
    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(PayPalAuthorizationRequest request, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray(BuildPurchaseUnit(request))
        };

        var paymentSource = BuildCardPaymentSource(request);
        body["payment_source"] = new JsonObject { ["card"] = paymentSource };

        // Stable PayPal-Request-Id => a double-click reuses the same PayPal order rather than creating a second.
        using var created = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, request.IdempotencyKey, ct);
        var root = created.RootElement;

        var payPalOrderId = root.GetProperty("id").GetString()!;
        var status = GetString(root, "status");

        // If the order still needs an explicit authorization step, do it now.
        if (!TryGetAuthorization(root, out _) && string.Equals(status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            using var authorized = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize",
                new JsonObject(), $"{request.IdempotencyKey}-authorize", ct);
            return BuildAuthorizationResult(payPalOrderId, authorized.RootElement);
        }

        // A challenge that needs the shopper in a browser is a hard stop per the task.
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            throw new PaymentApprovalRequiredException(
                $"PayPal requires shopper approval in a browser for order {payPalOrderId} (status PAYER_ACTION_REQUIRED).");

        return BuildAuthorizationResult(payPalOrderId, root);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount,
        string currencyCode, string idempotencyKey, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["amount"] = Money(amount, currencyCode),
            ["final_capture"] = true
        };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture",
            body, idempotencyKey, ct);
        var root = doc.RootElement;

        decimal capturedAmount = amount;
        decimal? fee = null, net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            capturedAmount = GetMoney(breakdown, "gross_amount") ?? amount;
            fee = GetMoney(breakdown, "paypal_fee");
            net = GetMoney(breakdown, "net_amount");
        }

        return new PayPalCaptureResult
        {
            CaptureId = root.GetProperty("id").GetString()!,
            Status = GetString(root, "status"),
            Amount = capturedAmount,
            PayPalFee = fee,
            NetAmount = net,
            CurrencyCode = currencyCode
        };
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void",
            null, null, ct);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currencyCode, CancellationToken ct = default)
    {
        var body = new JsonObject { ["amount"] = Money(amount, currencyCode) };
        try
        {
            using var doc = await SendAsync(HttpMethod.Post,
                $"/v2/payments/authorizations/{authorizationId}/reauthorize", body,
                $"reauth-{authorizationId}", ct);
            var root = doc.RootElement;
            return new PayPalAuthorizationResult
            {
                PayPalOrderId = string.Empty,
                AuthorizationId = root.GetProperty("id").GetString()!,
                Status = GetString(root, "status"),
                ExpiresAt = GetDate(root, "expiration_time")
            };
        }
        catch (PayPalApiException ex)
        {
            // PayPal declines a re-authorization it will not honour; surface something an operator can act on.
            throw new AuthorizationNotRenewableException(
                $"Authorization {authorizationId} can no longer be renewed ({ex.Name ?? "error"}: {ex.Message}). " +
                "The order must be re-placed and paid again.", ex);
        }
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, ct);
        var root = doc.RootElement;
        return new PayPalAuthorizationResult
        {
            PayPalOrderId = string.Empty,
            AuthorizationId = root.GetProperty("id").GetString()!,
            Status = GetString(root, "status"),
            ExpiresAt = GetDate(root, "expiration_time")
        };
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, CancellationToken ct = default)
    {
        JsonObject? body = null;
        if (amount is decimal value)
            body = new JsonObject { ["amount"] = Money(value, currencyCode) };

        // Scope the PayPal-Request-Id to this capture so a caller key reused across captures/runs never
        // collides at PayPal, while the caller's own key still governs domain-level idempotency.
        var requestId = $"refund-{captureId}-{idempotencyKey}";
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund",
            body, requestId, ct);
        var root = doc.RootElement;

        return new PayPalRefundResult
        {
            RefundId = root.GetProperty("id").GetString()!,
            Status = GetString(root, "status"),
            Amount = GetMoney(root, "amount") ?? amount ?? 0m,
            CurrencyCode = currencyCode
        };
    }

    public async Task<PayPalVaultResult> VaultCardAsync(PayPalCardDetails card, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["payment_source"] = new JsonObject { ["card"] = BuildCard(card) }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body,
            Guid.NewGuid().ToString(), ct);
        var root = doc.RootElement;

        string? brand = null, last4 = null, expiry = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var c))
        {
            brand = GetString(c, "brand");
            last4 = GetString(c, "last_digits");
            expiry = GetString(c, "expiry");
        }
        var (month, year) = ParseExpiry(expiry);

        return new PayPalVaultResult
        {
            VaultId = root.GetProperty("id").GetString()!,
            Brand = brand,
            Last4 = last4,
            ExpiryMonth = month,
            ExpiryYear = year
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        try
        {
            using var _ = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, ct);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            // Already gone from PayPal's vault — that is fine for a delete.
            _logger.LogWarning($"Vault token {vaultId} was already absent from PayPal ({ex.Message}).");
        }
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        var results = new List<PayPalTransaction>();

        // Cover the whole range by walking it in <=31-day windows, following pagination within each.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxReportingWindow;
            if (windowEnd > to) windowEnd = to;

            int page = 1, totalPages = 1;
            do
            {
                var path = "/v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(FormatReportingDate(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatReportingDate(windowEnd))}" +
                    $"&fields=all&page_size={ReportingPageSize}&page={page}";

                using var doc = await SendAsync(HttpMethod.Get, path, null, null, ct);
                var root = doc.RootElement;

                if (root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var tpVal))
                    totalPages = tpVal;

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                        results.Add(ParseTransaction(detail));
                }

                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd;
        }

        return results;
    }

    // ---- request builders ----

    private JsonObject BuildPurchaseUnit(PayPalAuthorizationRequest request)
    {
        var pu = new JsonObject
        {
            ["reference_id"] = request.ReferenceId,
            ["amount"] = Money(request.Amount, request.CurrencyCode)
        };
        if (!string.IsNullOrEmpty(request.CustomId))
        {
            pu["custom_id"] = request.CustomId;
            pu["invoice_id"] = $"eshop-{request.CustomId}-{request.IdempotencyKey}";
        }
        return pu;
    }

    private static JsonObject BuildCardPaymentSource(PayPalAuthorizationRequest request)
    {
        // Pay with a saved (vaulted) card, or a one-off card supplied on the request.
        if (!string.IsNullOrEmpty(request.VaultId))
            return new JsonObject { ["vault_id"] = request.VaultId };

        if (request.Card is null)
            throw new InvalidOperationException("Authorization request has neither a vaulted card nor card details.");

        return BuildCard(request.Card);
    }

    private static JsonObject BuildCard(PayPalCardDetails card)
    {
        var node = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode
        };
        if (!string.IsNullOrEmpty(card.Name)) node["name"] = card.Name;
        if (card.BillingAddress is not null)
        {
            var a = card.BillingAddress;
            var addr = new JsonObject();
            if (!string.IsNullOrEmpty(a.AddressLine1)) addr["address_line_1"] = a.AddressLine1;
            if (!string.IsNullOrEmpty(a.AddressLine2)) addr["address_line_2"] = a.AddressLine2;
            if (!string.IsNullOrEmpty(a.AdminArea2)) addr["admin_area_2"] = a.AdminArea2;
            if (!string.IsNullOrEmpty(a.AdminArea1)) addr["admin_area_1"] = a.AdminArea1;
            if (!string.IsNullOrEmpty(a.PostalCode)) addr["postal_code"] = a.PostalCode;
            if (!string.IsNullOrEmpty(a.CountryCode)) addr["country_code"] = a.CountryCode;
            if (addr.Count > 0) node["billing_address"] = addr;
        }
        return node;
    }

    private static JsonObject Money(decimal amount, string currencyCode) => new()
    {
        ["currency_code"] = currencyCode,
        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    // ---- response parsing ----

    private PayPalAuthorizationResult BuildAuthorizationResult(string payPalOrderId, JsonElement orderRoot)
    {
        if (!TryGetAuthorization(orderRoot, out var auth))
            throw new PayPalApiException(
                $"PayPal order {payPalOrderId} did not yield an authorization (status {GetString(orderRoot, "status")}).",
                (int)HttpStatusCode.BadGateway);

        string? brand = null, last4 = null, vaultId = null;
        if (orderRoot.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var card))
        {
            brand = GetString(card, "brand");
            last4 = GetString(card, "last_digits");
            if (card.TryGetProperty("attributes", out var attrs) && attrs.TryGetProperty("vault", out var vault))
                vaultId = GetString(vault, "id");
        }

        var summary = brand is null && last4 is null ? null : $"{brand} ending {last4}".Trim();

        return new PayPalAuthorizationResult
        {
            PayPalOrderId = payPalOrderId,
            AuthorizationId = auth.GetProperty("id").GetString()!,
            Status = GetString(auth, "status"),
            ExpiresAt = GetDate(auth, "expiration_time"),
            InstrumentSummary = summary,
            VaultId = vaultId
        };
    }

    private static bool TryGetAuthorization(JsonElement orderRoot, out JsonElement authorization)
    {
        authorization = default;
        if (!orderRoot.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments) &&
                payments.TryGetProperty("authorizations", out var auths) &&
                auths.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in auths.EnumerateArray())
                {
                    authorization = a;
                    return true;
                }
            }
        }
        return false;
    }

    private PayPalTransaction ParseTransaction(JsonElement detail)
    {
        var info = detail.TryGetProperty("transaction_info", out var ti) ? ti : detail;
        decimal? amount = GetMoney(info, "transaction_amount");
        decimal? fee = GetMoney(info, "fee_amount");
        string? currency = null;
        if (info.TryGetProperty("transaction_amount", out var ta))
            currency = GetString(ta, "currency_code");

        return new PayPalTransaction
        {
            TransactionId = GetString(info, "transaction_id") ?? string.Empty,
            Status = GetString(info, "transaction_status"),
            EventCode = GetString(info, "transaction_event_code"),
            Amount = amount,
            CurrencyCode = currency,
            Fee = fee,
            Date = GetDate(info, "transaction_initiation_date") ?? GetDate(info, "transaction_updated_date"),
            CustomId = GetString(info, "custom_field"),
            InvoiceId = GetString(info, "invoice_id")
        };
    }

    // ---- HTTP plumbing ----

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, JsonNode? body,
        string? requestId, CancellationToken ct)
    {
        var token = await _tokenProvider.GetTokenAsync(ct);
        var http = _httpFactory.CreateClient(PayPalTokenProvider.HttpClientName);

        using var message = new HttpRequestMessage(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(requestId))
            message.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        // Ask PayPal to return the full resource representation on writes.
        message.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        if (body is not null)
            message.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(message, ct);
        var content = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw BuildApiException(response, content);

        if (string.IsNullOrWhiteSpace(content))
            return JsonDocument.Parse("{}");

        // Some card challenges come back 200 with a payer-action link rather than an error.
        var doc = JsonDocument.Parse(content);
        if (string.Equals(GetString(doc.RootElement, "status"), "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            doc.Dispose();
            throw new PaymentApprovalRequiredException(
                "PayPal requires shopper approval in a browser (status PAYER_ACTION_REQUIRED).");
        }
        return doc;
    }

    private PayPalApiException BuildApiException(HttpResponseMessage response, string content)
    {
        var debugId = response.Headers.TryGetValues("PayPal-Debug-Id", out var ids)
            ? string.Join(",", ids) : null;
        string? name = null;
        string message = response.ReasonPhrase ?? "PayPal request failed";

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            name = GetString(root, "name") ?? GetString(root, "error");
            message = GetString(root, "message") ?? GetString(root, "error_description") ?? message;
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array &&
                details.GetArrayLength() > 0)
            {
                var first = details[0];
                var issue = GetString(first, "issue");
                var desc = GetString(first, "description");
                if (!string.IsNullOrEmpty(issue)) message = $"{message} [{issue}] {desc}".Trim();
            }
            if (debugId is null) debugId = GetString(root, "debug_id");
        }
        catch (JsonException)
        {
            if (!string.IsNullOrWhiteSpace(content)) message = $"{message}: {content}";
        }

        _logger.LogWarning($"PayPal {(int)response.StatusCode} {name}: {message} (debug_id={debugId}).");
        return new PayPalApiException(message, (int)response.StatusCode, debugId, name);
    }

    // ---- small json helpers ----

    private static string? GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var v) &&
           v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static decimal? GetMoney(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var money))
            return null;
        var value = GetString(money, "value");
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static DateTimeOffset? GetDate(JsonElement element, string property)
    {
        var raw = GetString(element, property);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt : null;
    }

    private static (int? Month, int? Year) ParseExpiry(string? expiry)
    {
        // PayPal returns "YYYY-MM".
        if (string.IsNullOrEmpty(expiry)) return (null, null);
        var parts = expiry.Split('-');
        if (parts.Length == 2 && int.TryParse(parts[0], out var year) && int.TryParse(parts[1], out var month))
            return (month, year);
        return (null, null);
    }

    private string FormatReportingDate(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}

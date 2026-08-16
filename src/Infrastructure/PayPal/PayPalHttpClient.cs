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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The concrete PayPal REST client. Every method maps to a documented PayPal v2/v3 REST operation:
/// Orders v2 for authorize, Payments v2 for capture/reauthorize/void/refund, Vault v3 for saved cards,
/// and Transaction Search v1 for reconciliation. Owns idempotency headers, retry/backoff, and error
/// translation. Card numbers pass straight through to PayPal and are never logged.
/// </summary>
public class PayPalHttpClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // PayPal transaction search accepts a range of at most 31 days per request.
    private static readonly TimeSpan MaxReportingWindow = TimeSpan.FromDays(31);

    private readonly HttpClient _httpClient;
    private readonly PayPalTokenProvider _tokenProvider;
    private readonly PayPalSettings _settings;

    public PayPalHttpClient(HttpClient httpClient, PayPalTokenProvider tokenProvider,
        IOptions<PayPalSettings> settings)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _settings = settings.Value;
    }

    private string BaseUrl => _settings.ResolveBaseUrl();

    public async Task<AuthorizeResult> AuthorizeWithCardAsync(decimal amount, string currency, string orderReference,
        CardDetails card, bool storeInVault, string requestId, CancellationToken cancellationToken = default)
    {
        var cardSource = BuildCardSource(card);
        if (storeInVault)
            cardSource["attributes"] = new { vault = new { store_in_vault = "ON_SUCCESS" } };

        var payload = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[] { PurchaseUnit(amount, currency, orderReference, requestId) },
            payment_source = new Dictionary<string, object?> { ["card"] = cardSource }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", payload, requestId, cancellationToken);
        return ParseAuthorizeResult(doc.RootElement);
    }

    public async Task<AuthorizeResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency,
        string orderReference, string vaultTokenId, string requestId, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[] { PurchaseUnit(amount, currency, orderReference, requestId) },
            payment_source = new { card = new { vault_id = vaultTokenId } }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", payload, requestId, cancellationToken);
        return ParseAuthorizeResult(doc.RootElement);
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            amount = Money(amount, currency),
            final_capture = true
        };

        using var doc = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", payload, requestId, cancellationToken);
        var root = doc.RootElement;

        var captureId = root.GetProperty("id").GetString()!;
        var status = root.GetProperty("status").GetString()!;

        if (TryReadBreakdown(root, out var gross, out var fee, out var net))
            return new CaptureResult(captureId, status, gross, fee, net, currency);

        // The capture POST does not always include the fee breakdown; fetch it authoritatively.
        using var captureDoc = await SendAsync(HttpMethod.Get,
            $"/v2/payments/captures/{captureId}", null, null, cancellationToken);
        TryReadBreakdown(captureDoc.RootElement, out gross, out fee, out net);
        var finalStatus = captureDoc.RootElement.TryGetProperty("status", out var s) ? s.GetString()! : status;
        return new CaptureResult(captureId, finalStatus, gross, fee, net, currency);
    }

    public async Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default)
    {
        var payload = new { amount = Money(amount, currency) };

        using var doc = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", payload, requestId, cancellationToken);
        var root = doc.RootElement;

        var newId = root.TryGetProperty("id", out var idEl) ? idEl.GetString()! : authorizationId;
        var status = root.TryGetProperty("status", out var st) ? st.GetString()! : Payment.AuthCreated;
        DateTimeOffset? expires = root.TryGetProperty("expiration_time", out var exp)
            && exp.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(exp.GetString(), out var e)
            ? e : null;
        return new ReauthorizeResult(newId, status, expires);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void", null, requestId, cancellationToken);
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string orderReference, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["custom_id"] = orderReference
        };
        if (amount is not null)
            payload["amount"] = Money(amount.Value, currency);

        // Scope the PayPal-Request-Id to this capture + the caller's key: unique per capture (so two
        // captures using the same caller key don't collide) yet stable across retries of the same refund.
        var payPalRequestId = $"refund-{captureId}-{idempotencyKey}";
        using var doc = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund", payload, payPalRequestId, cancellationToken);
        var root = doc.RootElement;

        var refundId = root.GetProperty("id").GetString()!;
        var status = root.GetProperty("status").GetString()!;
        var refundedAmount = amount ?? (TryReadMoney(root, "amount", out var v, out _) ? v : 0m);
        return new RefundResult(refundId, status, refundedAmount, currency);
    }

    public async Task<VaultResult> VaultCardAsync(CardDetails card, string requestId,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            payment_source = new Dictionary<string, object?> { ["card"] = BuildCardSource(card) }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", payload, requestId, cancellationToken);
        var root = doc.RootElement;

        var tokenId = root.GetProperty("id").GetString()!;
        string? customerId = root.TryGetProperty("customer", out var cust)
            && cust.TryGetProperty("id", out var custId) ? custId.GetString() : null;

        string brand = "CARD", last4 = "0000", expiry = card.Expiry;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var c))
        {
            brand = c.TryGetProperty("brand", out var b) ? b.GetString() ?? brand : brand;
            last4 = c.TryGetProperty("last_digits", out var l) ? l.GetString() ?? last4 : last4;
            expiry = c.TryGetProperty("expiry", out var e) ? e.GetString() ?? expiry : expiry;
        }
        return new VaultResult(tokenId, customerId, brand, last4, expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{vaultTokenId}", null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();

        // Cover the whole range: walk it in <=31-day windows, paging through each window fully.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxReportingWindow;
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            int totalPages;
            do
            {
                var query = $"/v1/reporting/transactions?start_date={FormatReportingDate(windowStart)}" +
                            $"&end_date={FormatReportingDate(windowEnd)}&fields=all&page_size=500&page={page}";

                using var doc = await SendAsync(HttpMethod.Get, query, null, null, cancellationToken);
                var root = doc.RootElement;

                totalPages = root.TryGetProperty("total_pages", out var tp) ? tp.GetInt32() : 1;

                if (root.TryGetProperty("transaction_details", out var details)
                    && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (detail.TryGetProperty("transaction_info", out var info))
                            results.Add(ParseTransaction(info));
                    }
                }

                page++;
            }
            while (page <= totalPages);

            // Nudge past the window end (dates are inclusive) to avoid re-fetching the boundary second.
            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    // ---- payload/parse helpers ----

    private static object PurchaseUnit(decimal amount, string currency, string orderReference, string requestId)
    {
        // The reference is already globally unique per order, so it serves as both the custom_id (used to
        // line the transaction up with the eShop order in reconciliation) and the invoice_id (which PayPal
        // requires to be unique per merchant).
        return new
        {
            custom_id = orderReference,
            invoice_id = orderReference,
            amount = Money(amount, currency)
        };
    }

    private static Dictionary<string, object?> BuildCardSource(CardDetails card)
    {
        var source = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["name"] = card.Name
        };

        if (card.BillingAddress is { } a)
        {
            source["billing_address"] = new
            {
                address_line_1 = a.AddressLine1,
                address_line_2 = a.AddressLine2,
                admin_area_2 = a.AdminArea2,
                admin_area_1 = a.AdminArea1,
                postal_code = a.PostalCode,
                country_code = a.CountryCode
            };
        }
        return source;
    }

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency,
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static AuthorizeResult ParseAuthorizeResult(JsonElement root)
    {
        var payPalOrderId = root.GetProperty("id").GetString()!;

        string? brand = null, last4 = null, vaultTokenId = null, vaultCustomerId = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var card))
        {
            brand = card.TryGetProperty("brand", out var b) ? b.GetString() : null;
            last4 = card.TryGetProperty("last_digits", out var l) ? l.GetString() : null;
            if (card.TryGetProperty("attributes", out var attrs) && attrs.TryGetProperty("vault", out var vault))
            {
                vaultTokenId = vault.TryGetProperty("id", out var vid) ? vid.GetString() : null;
                if (vault.TryGetProperty("customer", out var vc) && vc.TryGetProperty("id", out var vcid))
                    vaultCustomerId = vcid.GetString();
            }
        }

        var authorization = FindFirstAuthorization(root);
        var authId = authorization?.GetProperty("id").GetString()
            ?? throw new PayPalApiException(502, "NO_AUTHORIZATION", null,
                "PayPal accepted the order but returned no authorization to act on.");
        var authStatus = authorization.Value.TryGetProperty("status", out var st)
            ? st.GetString() ?? Payment.AuthCreated : Payment.AuthCreated;
        DateTimeOffset? expires = authorization.Value.TryGetProperty("expiration_time", out var exp)
            && exp.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(exp.GetString(), out var e)
            ? e : null;

        return new AuthorizeResult(payPalOrderId, authId, authStatus, expires, brand, last4,
            vaultTokenId, vaultCustomerId);
    }

    private static JsonElement? FindFirstAuthorization(JsonElement orderRoot)
    {
        if (!orderRoot.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments)
                && payments.TryGetProperty("authorizations", out var auths)
                && auths.ValueKind == JsonValueKind.Array)
            {
                foreach (var auth in auths.EnumerateArray())
                    return auth;
            }
        }
        return null;
    }

    private static bool TryReadBreakdown(JsonElement captureRoot, out decimal gross, out decimal fee, out decimal net)
    {
        gross = fee = net = 0m;
        if (!captureRoot.TryGetProperty("seller_receivable_breakdown", out var b))
            return false;

        var hasGross = TryReadMoney(b, "gross_amount", out gross, out _);
        TryReadMoney(b, "paypal_fee", out fee, out _);
        TryReadMoney(b, "net_amount", out net, out _);
        return hasGross;
    }

    private static bool TryReadMoney(JsonElement parent, string property, out decimal value, out string? currency)
    {
        value = 0m;
        currency = null;
        if (!parent.TryGetProperty(property, out var money))
            return false;
        if (money.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
            value = decimal.Parse(v.GetString()!, CultureInfo.InvariantCulture);
        if (money.TryGetProperty("currency_code", out var c))
            currency = c.GetString();
        return true;
    }

    private static PayPalTransaction ParseTransaction(JsonElement info)
    {
        var id = info.TryGetProperty("transaction_id", out var tid) ? tid.GetString() ?? "" : "";
        var status = info.TryGetProperty("transaction_status", out var ts) ? ts.GetString() ?? "" : "";
        var eventCode = info.TryGetProperty("transaction_event_code", out var ec) ? ec.GetString() : null;
        var invoiceId = info.TryGetProperty("invoice_id", out var inv) ? inv.GetString() : null;
        var customField = info.TryGetProperty("custom_field", out var cf) ? cf.GetString() : null;
        var referenceId = info.TryGetProperty("paypal_reference_id", out var rid) ? rid.GetString() : null;

        decimal amount = 0m;
        string currency = "";
        if (info.TryGetProperty("transaction_amount", out var amt))
        {
            if (amt.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
                amount = decimal.Parse(v.GetString()!, CultureInfo.InvariantCulture);
            if (amt.TryGetProperty("currency_code", out var c))
                currency = c.GetString() ?? "";
        }

        DateTimeOffset date = default;
        foreach (var dateProp in new[] { "transaction_initiation_date", "transaction_updated_date" })
        {
            if (info.TryGetProperty(dateProp, out var d) && d.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(d.GetString(), out var parsed))
            {
                date = parsed;
                break;
            }
        }

        return new PayPalTransaction(id, status, amount, currency, date, eventCode, invoiceId, customField, referenceId);
    }

    private static string FormatReportingDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "-0000";

    // ---- transport ----

    private async Task<JsonDocument> SendAsync(HttpMethod method, string pathOrUrl, object? body,
        string? requestId, CancellationToken cancellationToken)
    {
        const int maxAttempts = 4;
        var url = pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? pathOrUrl : BaseUrl + pathOrUrl;

        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, url);
            var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (!string.IsNullOrEmpty(requestId))
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);

            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return string.IsNullOrWhiteSpace(content)
                    ? JsonDocument.Parse("{}")
                    : JsonDocument.Parse(content);
            }

            // Retry transient failures (rate limit, server errors) with backoff.
            if (attempt < maxAttempts && IsTransient(response.StatusCode))
            {
                var delay = RetryDelay(response, attempt);
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            throw BuildApiException(response, content);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return delta;
        return TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
    }

    private static PayPalApiException BuildApiException(HttpResponseMessage response, string content)
    {
        string? issue = null;
        string? debugId = response.Headers.TryGetValues("PayPal-Debug-Id", out var ids)
            ? string.Join(",", ids) : null;
        var message = $"PayPal API call failed with HTTP {(int)response.StatusCode}.";

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.TryGetProperty("name", out var name)) issue = name.GetString();
            if (root.TryGetProperty("debug_id", out var dbg)) debugId = dbg.GetString() ?? debugId;

            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.TryGetProperty("issue", out var iss))
                    {
                        issue = iss.GetString() ?? issue;
                        var desc = detail.TryGetProperty("description", out var d) ? d.GetString() : null;
                        message = $"PayPal rejected the request ({issue}): {desc}";
                        break;
                    }
                }
            }
            else if (root.TryGetProperty("message", out var msg))
            {
                message = $"PayPal rejected the request: {msg.GetString()}";
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the generic message.
        }

        return new PayPalApiException((int)response.StatusCode, issue, debugId, message);
    }
}

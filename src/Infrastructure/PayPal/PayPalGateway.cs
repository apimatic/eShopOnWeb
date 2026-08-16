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
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The single place that talks HTTP to PayPal's REST APIs (Orders v2, Payments v2, Vault v3, Transaction
/// Search v1). Card numbers/CVV pass through here for a request but are never persisted or logged.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly PayPalTokenStore _tokenStore;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(HttpClient http, PayPalSettings settings, PayPalTokenStore tokenStore,
        ILogger<PayPalGateway> logger)
    {
        _http = http;
        _settings = settings;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    private string Currency => _settings.ResolveCurrency();

    // ------------------------------------------------------------------ Orders / Authorize

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(decimal amount, PayPalCardInstrument instrument,
        string requestId, string customId, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["custom_id"] = customId,
                    ["amount"] = Money(amount)
                }
            },
            ["payment_source"] = BuildCardSource(instrument)
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body,
            requestId: requestId, representation: true, cancellationToken);
        var order = doc!.RootElement;
        var payPalOrderId = order.GetProperty("id").GetString()!;

        // A card that PayPal answered with an approval challenge (e.g. 3-D Secure) is out of scope here.
        GuardAgainstApprovalChallenge(order, payPalOrderId);

        if (TryReadAuthorization(order, out var auth))
        {
            return auth;
        }

        // Some flows return the order APPROVED and expect a separate authorize call.
        var status = order.TryGetProperty("status", out var s) ? s.GetString() : null;
        if (string.Equals(status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            using var authDoc = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize",
                new Dictionary<string, object?>(), requestId: requestId + "-auth", representation: true, cancellationToken);
            var authOrder = authDoc!.RootElement;
            GuardAgainstApprovalChallenge(authOrder, payPalOrderId);
            if (TryReadAuthorization(authOrder, out var auth2))
            {
                return auth2;
            }
        }

        throw new PaymentException(
            $"PayPal did not return an authorization for order {payPalOrderId} (status: {status ?? "unknown"}).");
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null,
            requestId: null, representation: false, cancellationToken);
        return ReadAuthorizationObject(doc!.RootElement);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string requestId, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?> { ["amount"] = Money(amount) };
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body, requestId: requestId, representation: true, cancellationToken);
        return ReadAuthorizationObject(doc!.RootElement);
    }

    // ------------------------------------------------------------------ Capture / Void / Refund

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string customId,
        string requestId, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = Money(amount),
            ["final_capture"] = true,
            ["custom_id"] = customId
        };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture",
            body, requestId: requestId, representation: true, cancellationToken);
        var root = doc!.RootElement;

        var captureId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString()! : "UNKNOWN";
        var gross = ReadMoney(root, "amount") ?? amount;

        decimal? fee = null, net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            gross = ReadMoney(breakdown, "gross_amount") ?? gross;
            fee = ReadMoney(breakdown, "paypal_fee");
            net = ReadMoney(breakdown, "net_amount");
        }

        return new PayPalCaptureResult(captureId, status, gross, fee, net);
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null,
            requestId: requestId, representation: false, cancellationToken, allowEmptyResponse: true);
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string customId,
        string requestId, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?> { ["custom_id"] = customId };
        if (amount is decimal a)
        {
            body["amount"] = Money(a);
        }

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body,
            requestId: requestId, representation: true, cancellationToken);
        var root = doc!.RootElement;

        var refundId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString()! : "UNKNOWN";
        var refunded = ReadMoney(root, "amount") ?? amount ?? 0m;

        return new PayPalRefundResult(refundId, status, refunded);
    }

    // ------------------------------------------------------------------ Vault (saved cards)

    public async Task<PayPalVaultedCardResult> VaultCardAsync(PayPalRawCard card, string? customerId,
        string requestId, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = BuildRawCard(card) }
        };
        if (!string.IsNullOrEmpty(customerId))
        {
            body["customer"] = new Dictionary<string, object?> { ["id"] = customerId };
        }

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body,
            requestId: requestId, representation: false, cancellationToken);
        var root = doc!.RootElement;

        var vaultId = root.GetProperty("id").GetString()!;
        string? returnedCustomerId = null;
        if (root.TryGetProperty("customer", out var customer) && customer.TryGetProperty("id", out var cid))
        {
            returnedCustomerId = cid.GetString();
        }

        string brand = "UNKNOWN", lastDigits = "";
        string? expiry = null, name = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var c))
        {
            brand = c.TryGetProperty("brand", out var b) ? b.GetString() ?? brand : brand;
            lastDigits = c.TryGetProperty("last_digits", out var ld) ? ld.GetString() ?? "" : "";
            expiry = c.TryGetProperty("expiry", out var e) ? e.GetString() : null;
            name = c.TryGetProperty("name", out var n) ? n.GetString() : null;
        }

        return new PayPalVaultedCardResult(vaultId, returnedCustomerId, brand, lastDigits, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null,
            requestId: null, representation: false, cancellationToken, allowEmptyResponse: true);
    }

    // ------------------------------------------------------------------ Transaction Search / Reconciliation

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<PayPalTransaction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // PayPal's Transaction Search supports at most a 31-day window, so walk the range in chunks and
        // follow every page within each chunk. The report therefore covers the whole [from, to] range.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            var page = 1;
            var totalPages = 1;
            do
            {
                var query = "?start_date=" + Uri.EscapeDataString(FormatDate(windowStart)) +
                            "&end_date=" + Uri.EscapeDataString(FormatDate(windowEnd)) +
                            "&fields=all&balance_affecting_records_only=Y&page_size=500&page=" + page;

                JsonDocument? doc;
                try
                {
                    doc = await SendAsync(HttpMethod.Get, "/v1/reporting/transactions" + query, null,
                        requestId: null, representation: false, cancellationToken);
                }
                catch (PayPalApiException ex) when (ex.StatusCode == 404)
                {
                    // PayPal reporting lags live activity: for a window whose data is not yet available it
                    // answers 404 ("Data for the given start date is not available"). That is a legitimately
                    // empty window, not a failure — skip it and carry on covering the rest of the range.
                    _logger.LogInformation(
                        "PayPal reporting has no data yet for {Start}..{End}; treating the window as empty.",
                        FormatDate(windowStart), FormatDate(windowEnd));
                    break;
                }

                using var _doc = doc;
                var root = doc!.RootElement;

                if (root.TryGetProperty("total_pages", out var tp) && tp.ValueKind == JsonValueKind.Number)
                {
                    totalPages = tp.GetInt32();
                }

                if (root.TryGetProperty("transaction_details", out var details) &&
                    details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d in details.EnumerateArray())
                    {
                        if (!d.TryGetProperty("transaction_info", out var info))
                        {
                            continue;
                        }

                        var t = ReadTransaction(info);
                        var dedupeKey = t.TransactionId + "|" + (t.EventCode ?? "");
                        if (seen.Add(dedupeKey))
                        {
                            results.Add(t);
                        }
                    }
                }

                page++;
            }
            while (page <= totalPages && !cancellationToken.IsCancellationRequested);

            // Advance past this window; +1s avoids re-querying the exact boundary second.
            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    private static PayPalTransaction ReadTransaction(JsonElement info)
    {
        string txnId = info.TryGetProperty("transaction_id", out var id) ? id.GetString() ?? "" : "";
        string? eventCode = info.TryGetProperty("transaction_event_code", out var ec) ? ec.GetString() : null;
        string? status = info.TryGetProperty("transaction_status", out var st) ? st.GetString() : null;
        string? custom = info.TryGetProperty("custom_field", out var cf) ? cf.GetString() : null;
        string? invoice = info.TryGetProperty("invoice_id", out var inv) ? inv.GetString() : null;

        decimal? amount = ReadMoney(info, "transaction_amount");
        decimal? fee = ReadMoney(info, "fee_amount");
        string? currency = null;
        if (info.TryGetProperty("transaction_amount", out var amt) && amt.TryGetProperty("currency_code", out var cc))
        {
            currency = cc.GetString();
        }

        DateTimeOffset? date = null;
        if (info.TryGetProperty("transaction_initiation_date", out var di) &&
            DateTimeOffset.TryParse(di.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            date = parsed;
        }

        return new PayPalTransaction(txnId, eventCode, status, amount, currency, fee, date, invoice, custom);
    }

    // ------------------------------------------------------------------ HTTP plumbing

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string path, object? body, string? requestId,
        bool representation, CancellationToken cancellationToken, bool allowEmptyResponse = false)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            var token = await GetAccessTokenAsync(cancellationToken);

            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrEmpty(requestId))
            {
                request.Headers.Add("PayPal-Request-Id", requestId);
            }
            if (representation)
            {
                request.Headers.Add("Prefer", "return=representation");
            }
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var response = await _http.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
            {
                _tokenStore.Invalidate();
                continue;
            }

            if ((response.StatusCode == (HttpStatusCode)429 || (int)response.StatusCode >= 500) && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt * attempt), cancellationToken);
                continue;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw BuildApiException(response.StatusCode, content, path);
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                if (allowEmptyResponse)
                {
                    return null;
                }
                throw new PaymentException($"PayPal returned an empty response for {method} {path}.");
            }

            return JsonDocument.Parse(content);
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        return await _tokenStore.GetAsync(async ct =>
        {
            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
            {
                throw new PaymentException("PayPal credentials are not configured (PayPal:ClientId / PayPal:ClientSecret).");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _http.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw BuildApiException(response.StatusCode, content, "/v1/oauth2/token");
            }

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var accessToken = root.GetProperty("access_token").GetString()!;
            var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 300;
            _logger.LogInformation("Obtained a PayPal access token (expires in {ExpiresIn}s).", expiresIn);
            return (accessToken, expiresIn);
        }, cancellationToken);
    }

    private static PayPalApiException BuildApiException(HttpStatusCode statusCode, string content, string path)
    {
        string? name = null, message = null, debugId = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
                message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                debugId = root.TryGetProperty("debug_id", out var d) ? d.GetString() : null;
                if (root.TryGetProperty("error_description", out var ed)) // token endpoint shape
                {
                    message ??= ed.GetString();
                    name ??= root.TryGetProperty("error", out var er) ? er.GetString() : null;
                }
            }
        }
        catch (JsonException)
        {
            // fall through with raw content
        }

        var summary = message ?? name ?? $"PayPal request to {path} failed with HTTP {(int)statusCode}.";
        return new PayPalApiException(
            $"PayPal error on {path}: {summary} (HTTP {(int)statusCode}{(name is null ? "" : $", {name}")}{(debugId is null ? "" : $", debug id {debugId}")}).",
            (int)statusCode, name, debugId);
    }

    // ------------------------------------------------------------------ JSON helpers

    private object Money(decimal amount) => new Dictionary<string, object?>
    {
        ["currency_code"] = Currency,
        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private object BuildCardSource(PayPalCardInstrument instrument)
    {
        if (instrument.IsVaulted)
        {
            return new Dictionary<string, object?>
            {
                ["card"] = new Dictionary<string, object?> { ["vault_id"] = instrument.VaultId }
            };
        }

        return new Dictionary<string, object?> { ["card"] = BuildRawCard(instrument.RawCard!) };
    }

    private static Dictionary<string, object?> BuildRawCard(PayPalRawCard card)
    {
        var dict = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["name"] = card.Name
        };

        if (card.BillingAddress is { } addr)
        {
            dict["billing_address"] = new Dictionary<string, object?>
            {
                ["address_line_1"] = addr.AddressLine1,
                ["address_line_2"] = addr.AddressLine2,
                ["admin_area_2"] = addr.AdminArea2,
                ["admin_area_1"] = addr.AdminArea1,
                ["postal_code"] = addr.PostalCode,
                ["country_code"] = addr.CountryCode
            };
        }

        return dict;
    }

    private static bool TryReadAuthorization(JsonElement order, out PayPalAuthorizationResult result)
    {
        result = default!;
        if (!order.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var orderId = order.TryGetProperty("id", out var oid) ? oid.GetString() ?? "" : "";
        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments) &&
                payments.TryGetProperty("authorizations", out var auths) &&
                auths.ValueKind == JsonValueKind.Array)
            {
                foreach (var auth in auths.EnumerateArray())
                {
                    var authId = auth.TryGetProperty("id", out var aid) ? aid.GetString() : null;
                    if (authId is null)
                    {
                        continue;
                    }
                    var status = auth.TryGetProperty("status", out var st) ? st.GetString() ?? "CREATED" : "CREATED";
                    result = new PayPalAuthorizationResult(orderId, authId, status, ReadExpiry(auth));
                    return true;
                }
            }
        }

        return false;
    }

    private static PayPalAuthorizationResult ReadAuthorizationObject(JsonElement auth)
    {
        var authId = auth.GetProperty("id").GetString()!;
        var status = auth.TryGetProperty("status", out var st) ? st.GetString() ?? "UNKNOWN" : "UNKNOWN";
        return new PayPalAuthorizationResult(string.Empty, authId, status, ReadExpiry(auth));
    }

    private static void GuardAgainstApprovalChallenge(JsonElement order, string orderId)
    {
        var status = order.TryGetProperty("status", out var s) ? s.GetString() : null;
        var challenge = string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase);

        if (!challenge && order.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                var rel = link.TryGetProperty("rel", out var r) ? r.GetString() : null;
                if (string.Equals(rel, "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    challenge = true;
                    break;
                }
            }
        }

        if (challenge)
        {
            throw new PaymentApprovalRequiredException(
                $"PayPal requires the shopper to approve payment for order {orderId} in a browser " +
                "(e.g. a 3-D Secure challenge). This browser-free integration stops here rather than building an approval round-trip.");
        }
    }

    private static DateTimeOffset? ReadExpiry(JsonElement element)
    {
        if (element.TryGetProperty("expiration_time", out var exp) &&
            DateTimeOffset.TryParse(exp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
        {
            return value;
        }
        return null;
    }

    private static decimal? ReadMoney(JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var money) &&
            money.TryGetProperty("value", out var value) &&
            decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}

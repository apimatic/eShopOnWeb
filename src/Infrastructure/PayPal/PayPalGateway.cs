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
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Typed client for the PayPal REST APIs, hand-written against the PayPal OpenAPI specification
/// in <c>api-specs/paypal</c> (Checkout Orders v2, Payments v2, Vault Payment Tokens v3 and
/// Transaction Search v1). Handles OAuth2 client-credentials token acquisition/caching and speaks
/// the domain's language. All request paths are relative to the configured base address.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    // PayPal reporting allows at most a 31-day window per query; we chunk larger ranges.
    private static readonly TimeSpan MaxReportWindow = TimeSpan.FromDays(31);
    private const int ReportPageSize = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalGateway> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalGateway(HttpClient http, PayPalSettings settings, IAppLogger<PayPalGateway> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
    }

    // ---------------------------------------------------------------- Orders / authorize

    public async Task<AuthorizeOrderResult> AuthorizeOrderAsync(AuthorizeOrderRequest request, CancellationToken cancellationToken = default)
    {
        // 1) Create a Checkout order for the amount (intent AUTHORIZE, no payment source yet).
        var createBody = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    invoice_id = request.InvoiceId,
                    custom_id = request.CustomId,
                    amount = new { currency_code = request.CurrencyCode, value = FormatAmount(request.Amount) }
                }
            }
        };

        var created = await SendAsync(HttpMethod.Post, "v2/checkout/orders", createBody,
            requestId: $"{request.IdempotencyKey}-create", preferRepresentation: true, cancellationToken);
        var payPalOrderId = created.GetProperty("id").GetString()!;

        // 2) Authorize the order against the supplied card or saved (vaulted) card.
        var authBody = new { payment_source = new { card = BuildCardSource(request) } };
        var authorized = await SendAsync(HttpMethod.Post, $"v2/checkout/orders/{payPalOrderId}/authorize", authBody,
            requestId: $"{request.IdempotencyKey}-auth", preferRepresentation: true, cancellationToken);

        var status = authorized.TryGetProperty("status", out var s) ? s.GetString() : null;

        // If PayPal wants the shopper to approve in a browser, stop — we do not build an approval round-trip.
        if (RequiresPayerAction(status, authorized))
        {
            throw new PaymentApprovalRequiredException(
                $"PayPal requires shopper approval in a browser (status: {status}) to authorize order {request.CustomId}. " +
                "This integration is designed to run without a browser step; reporting the challenge instead of building an approval flow.");
        }

        var authorization = FindFirstAuthorization(authorized)
            ?? throw new PaymentException($"PayPal did not return an authorization for order {request.CustomId} (status: {status}).");

        var authId = authorization.GetProperty("id").GetString()!;
        var authStatus = authorization.TryGetProperty("status", out var a) ? a.GetString() ?? "CREATED" : "CREATED";
        DateTimeOffset? expiresAt = TryGetDate(authorization, "expiration_time");
        var (brand, last4) = ReadCardDescriptor(authorized);

        return new AuthorizeOrderResult(payPalOrderId, authId, authStatus, expiresAt, brand, last4);
    }

    // ---------------------------------------------------------------- Capture / reauthorize / void

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currencyCode,
        string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = new { currency_code = currencyCode, value = FormatAmount(amount) },
            final_capture = true,
            invoice_id = invoiceId
        };

        var captured = await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/capture", body,
            requestId: idempotencyKey, preferRepresentation: true, cancellationToken);

        var captureId = captured.GetProperty("id").GetString()!;
        var status = captured.TryGetProperty("status", out var st) ? st.GetString() ?? "COMPLETED" : "COMPLETED";

        decimal gross = amount;
        decimal? fee = null, net = null;
        var currency = currencyCode;
        if (captured.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            gross = ReadMoneyValue(breakdown, "gross_amount") ?? amount;
            fee = ReadMoneyValue(breakdown, "paypal_fee");
            net = ReadMoneyValue(breakdown, "net_amount");
            currency = ReadMoneyCurrency(breakdown, "gross_amount") ?? currencyCode;
        }

        return new CaptureResult(captureId, status, gross, fee, net, currency);
    }

    public async Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new { amount = new { currency_code = currencyCode, value = FormatAmount(amount) } };

        var reauth = await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/reauthorize", body,
            requestId: idempotencyKey, preferRepresentation: true, cancellationToken);

        var newAuthId = reauth.GetProperty("id").GetString()!;
        var status = reauth.TryGetProperty("status", out var st) ? st.GetString() ?? "CREATED" : "CREATED";
        var expiresAt = TryGetDate(reauth, "expiration_time");
        return new ReauthorizeResult(newAuthId, status, expiresAt);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void", body: null,
            requestId: idempotencyKey, preferRepresentation: true, cancellationToken);
    }

    // ---------------------------------------------------------------- Refund

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode,
        string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object body = amount is decimal a
            ? new { amount = new { currency_code = currencyCode, value = FormatAmount(a) }, invoice_id = invoiceId }
            : new { invoice_id = invoiceId };

        var refunded = await SendAsync(HttpMethod.Post, $"v2/payments/captures/{captureId}/refund", body,
            requestId: idempotencyKey, preferRepresentation: true, cancellationToken);

        var refundId = refunded.GetProperty("id").GetString()!;
        var status = refunded.TryGetProperty("status", out var st) ? st.GetString() ?? "COMPLETED" : "COMPLETED";
        var refundedAmount = ReadMoneyValue(refunded, "amount") ?? amount ?? 0m;
        var currency = ReadMoneyCurrency(refunded, "amount") ?? currencyCode;
        return new RefundResult(refundId, status, refundedAmount, currency);
    }

    // ---------------------------------------------------------------- Vault

    public async Task<VaultCardResult> VaultCardAsync(VaultCardRequest request, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            customer = new { merchant_customer_id = request.MerchantCustomerId },
            payment_source = new { card = BuildRawCard(request.Card) }
        };

        var token = await SendAsync(HttpMethod.Post, "v3/vault/payment-tokens", body,
            requestId: request.IdempotencyKey, preferRepresentation: false, cancellationToken);

        var vaultId = token.GetProperty("id").GetString()!;
        string? brand = null, last4 = null, expiry = null;
        if (token.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var card))
        {
            brand = card.TryGetProperty("brand", out var b) ? b.GetString() : null;
            last4 = card.TryGetProperty("last_digits", out var l) ? l.GetString() : null;
            expiry = card.TryGetProperty("expiry", out var e) ? e.GetString() : null;
        }

        return new VaultCardResult(vaultId, brand, last4, expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"v3/vault/payment-tokens/{vaultId}", body: null,
            requestId: null, preferRepresentation: false, cancellationToken);
    }

    // ---------------------------------------------------------------- Transaction search

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var results = new List<PayPalTransaction>();
        var seen = new HashSet<string>();

        // Chunk the range into <=31-day windows (a reporting API constraint).
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxReportWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }
            if (windowEnd <= windowStart)
            {
                windowEnd = windowStart.AddSeconds(1);
            }

            await ReadTransactionWindowAsync(windowStart, windowEnd, results, seen, cancellationToken);
            windowStart = windowEnd;
        }

        return results;
    }

    private async Task ReadTransactionWindowAsync(DateTimeOffset windowStart, DateTimeOffset windowEnd,
        List<PayPalTransaction> results, HashSet<string> seen, CancellationToken cancellationToken)
    {
        var page = 1;
        var totalPages = 1;
        do
        {
            var query = "v1/reporting/transactions" +
                        $"?start_date={Uri.EscapeDataString(FormatReportDate(windowStart))}" +
                        $"&end_date={Uri.EscapeDataString(FormatReportDate(windowEnd))}" +
                        "&fields=all" +
                        $"&page_size={ReportPageSize}" +
                        $"&page={page}";

            var response = await SendAsync(HttpMethod.Get, query, body: null,
                requestId: null, preferRepresentation: false, cancellationToken);

            totalPages = response.TryGetProperty("total_pages", out var tp) && tp.ValueKind == JsonValueKind.Number
                ? tp.GetInt32()
                : 0;

            if (response.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (!detail.TryGetProperty("transaction_info", out var info))
                    {
                        continue;
                    }

                    var transactionId = info.TryGetProperty("transaction_id", out var t) ? t.GetString() : null;
                    if (transactionId is null || !seen.Add(transactionId))
                    {
                        continue; // de-duplicate across pages/windows
                    }

                    results.Add(new PayPalTransaction(
                        TransactionId: transactionId,
                        InvoiceId: info.TryGetProperty("invoice_id", out var inv) ? inv.GetString() : null,
                        EventCode: info.TryGetProperty("transaction_event_code", out var ec) ? ec.GetString() : null,
                        Amount: ReadMoneyValue(info, "transaction_amount"),
                        CurrencyCode: ReadMoneyCurrency(info, "transaction_amount"),
                        FeeAmount: ReadMoneyValue(info, "fee_amount"),
                        InitiationDate: TryGetDate(info, "transaction_initiation_date"),
                        Status: info.TryGetProperty("transaction_status", out var ts) ? ts.GetString() : null));
                }
            }

            page++;
        }
        while (page <= totalPages);
    }

    // ---------------------------------------------------------------- HTTP + token plumbing

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, string? requestId,
        bool preferRepresentation, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", Truncate(requestId, 108));
        }
        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ParseError(response.StatusCode, content);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default; // e.g. 204 No Content
        }

        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        // 60s safety margin so a token doesn't expire mid-request.
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt - TimeSpan.FromSeconds(60))
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt - TimeSpan.FromSeconds(60))
            {
                return _accessToken;
            }

            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw ParseError(response.StatusCode, content);
            }

            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            _accessToken = root.GetProperty("access_token").GetString();
            var expiresIn = root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number
                ? e.GetInt32()
                : 300;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

            _logger.LogInformation("Acquired PayPal access token (expires in {0}s).", expiresIn);
            return _accessToken!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    // ---------------------------------------------------------------- request builders

    private static object BuildCardSource(AuthorizeOrderRequest request)
    {
        if (!string.IsNullOrEmpty(request.VaultId))
        {
            return new { vault_id = request.VaultId };
        }
        if (request.Card is null)
        {
            throw new PaymentException("A card or a saved card is required to authorize a payment.");
        }
        return BuildRawCard(request.Card);
    }

    private static object BuildRawCard(CardDetails card)
    {
        return new
        {
            number = card.Number,
            expiry = card.Expiry,
            security_code = card.SecurityCode,
            name = card.CardholderName,
            billing_address = card.BillingAddress is null ? null : new
            {
                address_line_1 = card.BillingAddress.AddressLine1,
                address_line_2 = card.BillingAddress.AddressLine2,
                admin_area_2 = card.BillingAddress.City,
                admin_area_1 = card.BillingAddress.State,
                postal_code = card.BillingAddress.PostalCode,
                country_code = card.BillingAddress.CountryCode
            }
        };
    }

    // ---------------------------------------------------------------- response readers

    private static JsonElement? FindFirstAuthorization(JsonElement order)
    {
        if (!order.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments)
                && payments.TryGetProperty("authorizations", out var auths)
                && auths.ValueKind == JsonValueKind.Array)
            {
                foreach (var auth in auths.EnumerateArray())
                {
                    return auth;
                }
            }
        }
        return null;
    }

    private static (string? brand, string? last4) ReadCardDescriptor(JsonElement order)
    {
        if (order.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var card))
        {
            var brand = card.TryGetProperty("brand", out var b) ? b.GetString() : null;
            var last4 = card.TryGetProperty("last_digits", out var l) ? l.GetString() : null;
            return (brand, last4);
        }
        return (null, null);
    }

    private static bool RequiresPayerAction(string? status, JsonElement order)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (order.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                var rel = link.TryGetProperty("rel", out var r) ? r.GetString() : null;
                if (string.Equals(rel, "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static decimal? ReadMoneyValue(JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var money)
            && money.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    private static string? ReadMoneyCurrency(JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var money) && money.TryGetProperty("currency_code", out var c))
        {
            return c.GetString();
        }
        return null;
    }

    private static DateTimeOffset? TryGetDate(JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    private static PayPalApiException ParseError(HttpStatusCode statusCode, string content)
    {
        string? name = null, message = null, debugId = null;
        var issues = new List<string>();

        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                using var document = JsonDocument.Parse(content);
                var root = document.RootElement;
                name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
                message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                debugId = root.TryGetProperty("debug_id", out var d) ? d.GetString() : null;
                if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (detail.TryGetProperty("issue", out var issue))
                        {
                            var text = issue.GetString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                issues.Add(text);
                            }
                        }
                    }
                }
                // OAuth token errors use error / error_description instead of the standard error model.
                if (name is null && root.TryGetProperty("error", out var err))
                {
                    name = err.GetString();
                    message ??= root.TryGetProperty("error_description", out var ed) ? ed.GetString() : null;
                }
            }
            catch (JsonException)
            {
                message = content;
            }
        }

        return new PayPalApiException(statusCode, name, message, debugId, issues, content);
    }

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static string FormatReportDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "-0000";

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}

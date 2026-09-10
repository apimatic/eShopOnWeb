using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// HTTP implementation of <see cref="IPayPalClient"/> against the PayPal REST API
/// (Orders v2, Payments v2, Payment Method Tokens v3 and Transaction Search v1). Uses plain
/// HTTP with a cached client-credentials access token. Card details are only ever sent to
/// PayPal in request bodies; they are never logged.
/// </summary>
public class PayPalClient : IPayPalClient
{
    // PayPal caps a transaction-search request at 31 days; use a safe sub-31-day window.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(30);
    private const int SearchPageSize = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalClient> _logger;
    private readonly string _baseUrl;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(HttpClient http, PayPalSettings settings, ILogger<PayPalClient> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
        _baseUrl = settings.ResolveBaseUrl();
    }

    public string Currency => _settings.Currency;

    public async Task<AuthorizationResult> AuthorizeOrderWithCardAsync(decimal amount, string currency,
        string invoiceId, CardDetails card, string idempotencyKey, CancellationToken ct = default)
    {
        var cardBody = BuildCardBody(card);
        cardBody["attributes"] = new Dictionary<string, object?>
        {
            ["verification"] = new Dictionary<string, object?> { ["method"] = "SCA_WHEN_REQUIRED" }
        };
        return await CreateAuthorizeOrderAsync(amount, currency, invoiceId,
            new Dictionary<string, object?> { ["card"] = cardBody }, idempotencyKey, ct);
    }

    public async Task<AuthorizationResult> AuthorizeOrderWithVaultAsync(decimal amount, string currency,
        string invoiceId, string vaultId, string idempotencyKey, CancellationToken ct = default)
    {
        var paymentSource = new Dictionary<string, object?>
        {
            ["card"] = new Dictionary<string, object?> { ["vault_id"] = vaultId }
        };
        return await CreateAuthorizeOrderAsync(amount, currency, invoiceId, paymentSource, idempotencyKey, ct);
    }

    private async Task<AuthorizationResult> CreateAuthorizeOrderAsync(decimal amount, string currency,
        string invoiceId, Dictionary<string, object?> paymentSource, string idempotencyKey, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["invoice_id"] = invoiceId,
                    ["amount"] = Amount(amount, currency)
                }
            },
            ["payment_source"] = paymentSource
        };

        _logger.LogInformation("Creating PayPal AUTHORIZE order invoice={Invoice} requestId={RequestId} amount={Amount} {Currency}.",
            invoiceId, idempotencyKey, amount, currency);

        using var request = BuildJsonRequest(HttpMethod.Post, "/v2/checkout/orders", body);
        request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        using var doc = await SendAsync(request, ct);
        var root = doc.RootElement;

        var orderStatus = GetString(root, "status") ?? "UNKNOWN";
        if (RequiresBuyerAction(root, orderStatus))
        {
            throw new PayPalChallengeRequiredException(
                $"PayPal returned order status '{orderStatus}', which requires the shopper to approve the payment in a browser. This integration does not perform a browser approval round-trip.");
        }

        var payPalOrderId = GetString(root, "id")
            ?? throw new PayPalException(200, null, null, null, "PayPal order response did not contain an order id.");

        if (!TryGetAuthorization(root, out var authId, out var authStatus))
        {
            throw new PayPalException(200, null, null, null,
                $"PayPal order {payPalOrderId} status was '{orderStatus}' but contained no authorization to act on.");
        }

        return new AuthorizationResult(payPalOrderId, authId!, authStatus ?? "CREATED");
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount,
        string currency, string idempotencyKey, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = Amount(amount, currency),
            ["final_capture"] = true
        };

        using var request = BuildJsonRequest(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body);
        request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        using var doc = await SendAsync(request, ct);
        var root = doc.RootElement;

        var captureId = GetString(root, "id")
            ?? throw new PayPalException(200, null, null, null, "PayPal capture response did not contain a capture id.");
        var status = GetString(root, "status") ?? "UNKNOWN";

        decimal gross = amount;
        decimal? fee = null;
        decimal? net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            gross = MoneyValue(breakdown, "gross_amount") ?? amount;
            fee = MoneyValue(breakdown, "paypal_fee");
            net = MoneyValue(breakdown, "net_amount");
        }

        return new CaptureResult(captureId, status, gross, fee, net);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/payments/authorizations/{authorizationId}/void");
        using var _ = await SendAsync(request, ct);
    }

    public async Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["amount"] = Amount(amount, currency) };
        using var request = BuildJsonRequest(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        using var doc = await SendAsync(request, ct);
        var root = doc.RootElement;
        var newAuthId = GetString(root, "id")
            ?? throw new PayPalException(200, null, null, null, "PayPal reauthorize response did not contain an authorization id.");
        return new ReauthorizeResult(newAuthId, GetString(root, "status") ?? "CREATED");
    }

    public async Task<string> GetAuthorizationStatusAsync(string authorizationId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/v2/payments/authorizations/{authorizationId}");
        using var doc = await SendAsync(request, ct);
        return GetString(doc.RootElement, "status") ?? "UNKNOWN";
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>();
        if (amount is decimal value)
        {
            body["amount"] = Amount(value, currency);
        }

        using var request = BuildJsonRequest(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body);
        request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        using var doc = await SendAsync(request, ct);
        var root = doc.RootElement;
        var refundId = GetString(root, "id")
            ?? throw new PayPalException(200, null, null, null, "PayPal refund response did not contain a refund id.");
        return new RefundResult(refundId, GetString(root, "status") ?? "UNKNOWN");
    }

    public async Task<VaultedCardResult> VaultCardAsync(CardDetails card, CancellationToken ct = default)
    {
        // Step 1: create a setup token holding the raw card.
        var setupBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = BuildCardBody(card) }
        };
        using var setupReq = BuildJsonRequest(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody);
        setupReq.Headers.TryAddWithoutValidation("PayPal-Request-Id", Guid.NewGuid().ToString("N"));

        string setupTokenId;
        using (var setupDoc = await SendAsync(setupReq, ct))
        {
            setupTokenId = GetString(setupDoc.RootElement, "id")
                ?? throw new PayPalException(200, null, null, null, "PayPal setup-token response did not contain an id.");
        }

        // Step 2: exchange the setup token for a permanent payment-method token.
        var tokenBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?> { ["id"] = setupTokenId, ["type"] = "SETUP_TOKEN" }
            }
        };
        using var tokenReq = BuildJsonRequest(HttpMethod.Post, "/v3/vault/payment-tokens", tokenBody);
        tokenReq.Headers.TryAddWithoutValidation("PayPal-Request-Id", Guid.NewGuid().ToString("N"));

        using var tokenDoc = await SendAsync(tokenReq, ct);
        var root = tokenDoc.RootElement;

        var vaultId = GetString(root, "id")
            ?? throw new PayPalException(200, null, null, null, "PayPal payment-token response did not contain an id.");
        string? customerId = null;
        if (root.TryGetProperty("customer", out var customer))
        {
            customerId = GetString(customer, "id");
        }

        string brand = "CARD";
        string last = "";
        string expiry = "";
        string? name = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var c))
        {
            brand = GetString(c, "brand") ?? "CARD";
            last = GetString(c, "last_digits") ?? "";
            expiry = GetString(c, "expiry") ?? "";
            name = GetString(c, "name");
        }

        return new VaultedCardResult(vaultId, customerId, brand, last, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{_baseUrl}/v3/vault/payment-tokens/{vaultId}");
        using var _ = await SendAsync(request, ct);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<PayPalTransaction>();

        // Split the requested range into PayPal's <=31-day windows so the whole range is covered.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxSearchWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await SearchWindowAsync(windowStart, windowEnd, results, ct);

            // Advance a tick past the window end to avoid overlapping boundary transactions.
            windowStart = windowEnd.AddTicks(1);
        }

        return results;
    }

    private async Task SearchWindowAsync(DateTimeOffset start, DateTimeOffset end,
        List<PayPalTransaction> results, CancellationToken ct)
    {
        var page = 1;
        int totalPages;
        do
        {
            var url = $"{_baseUrl}/v1/reporting/transactions" +
                      $"?start_date={Uri.EscapeDataString(FormatDate(start))}" +
                      $"&end_date={Uri.EscapeDataString(FormatDate(end))}" +
                      $"&fields=all&page_size={SearchPageSize}&page={page}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var doc = await SendAsync(request, ct);
            var root = doc.RootElement;

            if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.TryGetProperty("transaction_info", out var info))
                    {
                        results.Add(ParseTransaction(info));
                    }
                }
            }

            totalPages = root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var tpv) ? tpv : 1;
            page++;
        }
        while (page <= totalPages);
    }

    private static PayPalTransaction ParseTransaction(JsonElement info)
    {
        var id = GetString(info, "transaction_id") ?? "";
        var status = GetString(info, "transaction_status");
        var invoiceId = GetString(info, "invoice_id");

        decimal? amount = null;
        string? currency = null;
        if (info.TryGetProperty("transaction_amount", out var amt))
        {
            amount = ParseDecimal(GetString(amt, "value"));
            currency = GetString(amt, "currency_code");
        }

        decimal? fee = null;
        if (info.TryGetProperty("fee_amount", out var feeEl))
        {
            fee = ParseDecimal(GetString(feeEl, "value"));
        }

        DateTimeOffset? initiated = null;
        var initiatedRaw = GetString(info, "transaction_initiation_date");
        if (initiatedRaw is not null && DateTimeOffset.TryParse(initiatedRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed))
        {
            initiated = parsed;
        }

        return new PayPalTransaction(id, status, amount, currency, invoiceId, fee, initiated);
    }

    // ---- helpers -------------------------------------------------------------------------

    private HttpRequestMessage BuildJsonRequest(HttpMethod method, string path, object body)
    {
        var request = new HttpRequestMessage(method, $"{_baseUrl}{path}")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };
        return request;
    }

    /// <summary>Sends a request with a valid bearer token, translating error responses to <see cref="PayPalException"/>.</summary>
    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(request, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw ParseError((int)response.StatusCode, payload);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return JsonDocument.Parse("{}");
        }

        return JsonDocument.Parse(payload);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

            using var response = await _http.SendAsync(request, ct);
            var payload = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw ParseError((int)response.StatusCode, payload);
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var accessToken = GetString(root, "access_token")
                ?? throw new PayPalException((int)response.StatusCode, null, null, null, "PayPal token response did not contain an access token.");
            var expiresIn = root.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out var s) ? s : 300;

            _accessToken = accessToken;
            // Refresh a minute early to avoid using a token that expires mid-flight.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            _logger.LogInformation("Obtained PayPal access token (expires in {ExpiresIn}s).", expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static PayPalException ParseError(int statusCode, string payload)
    {
        string? name = null;
        string? issue = null;
        string? debugId = null;
        string message = $"PayPal request failed with HTTP {statusCode}.";

        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                name = GetString(root, "name") ?? GetString(root, "error");
                debugId = GetString(root, "debug_id");
                var description = GetString(root, "message") ?? GetString(root, "error_description");
                if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
                    && details.GetArrayLength() > 0)
                {
                    var first = details[0];
                    issue = GetString(first, "issue");
                    description ??= GetString(first, "description");
                }
                message = $"PayPal HTTP {statusCode}{(name is null ? "" : $" {name}")}{(issue is null ? "" : $"/{issue}")}: {description ?? "no description"}"
                          + (debugId is null ? "" : $" (debug_id {debugId})");
            }
            catch (JsonException)
            {
                message = $"PayPal request failed with HTTP {statusCode}: {payload}";
            }
        }

        return new PayPalException(statusCode, name, issue, debugId, message);
    }

    private Dictionary<string, object?> BuildCardBody(CardDetails card)
    {
        var body = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry
        };
        if (!string.IsNullOrWhiteSpace(card.SecurityCode)) body["security_code"] = card.SecurityCode;
        if (!string.IsNullOrWhiteSpace(card.Name)) body["name"] = card.Name;

        var billing = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(card.BillingAddressLine1)) billing["address_line_1"] = card.BillingAddressLine1;
        if (!string.IsNullOrWhiteSpace(card.BillingAddressLine2)) billing["address_line_2"] = card.BillingAddressLine2;
        if (!string.IsNullOrWhiteSpace(card.BillingAdminArea1)) billing["admin_area_1"] = card.BillingAdminArea1;
        if (!string.IsNullOrWhiteSpace(card.BillingAdminArea2)) billing["admin_area_2"] = card.BillingAdminArea2;
        if (!string.IsNullOrWhiteSpace(card.BillingPostalCode)) billing["postal_code"] = card.BillingPostalCode;
        if (!string.IsNullOrWhiteSpace(card.BillingCountryCode)) billing["country_code"] = card.BillingCountryCode;
        if (billing.Count > 0) body["billing_address"] = billing;

        return body;
    }

    private static Dictionary<string, object?> Amount(decimal amount, string currency) => new()
    {
        ["currency_code"] = currency,
        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static bool RequiresBuyerAction(JsonElement root, string orderStatus)
    {
        if (string.Equals(orderStatus, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                if (string.Equals(GetString(link, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool TryGetAuthorization(JsonElement root, out string? authId, out string? authStatus)
    {
        authId = null;
        authStatus = null;
        if (root.TryGetProperty("purchase_units", out var units) && units.ValueKind == JsonValueKind.Array)
        {
            foreach (var unit in units.EnumerateArray())
            {
                if (unit.TryGetProperty("payments", out var payments)
                    && payments.TryGetProperty("authorizations", out var auths)
                    && auths.ValueKind == JsonValueKind.Array && auths.GetArrayLength() > 0)
                {
                    var auth = auths[0];
                    authId = GetString(auth, "id");
                    authStatus = GetString(auth, "status");
                    return authId is not null;
                }
            }
        }
        return false;
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? MoneyValue(JsonElement parent, string property)
        => parent.TryGetProperty(property, out var money) ? ParseDecimal(GetString(money, "value")) : null;

    private static decimal? ParseDecimal(string? value)
        => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string FormatDate(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}

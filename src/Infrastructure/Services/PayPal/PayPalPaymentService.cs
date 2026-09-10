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
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Talks to the PayPal REST API over plain HTTP (Orders v2, Payments v2, Vault v3, Transaction
/// Search v1). Manages its own OAuth2 access token (cached until shortly before it expires) and
/// never persists or logs raw card data — only PayPal-owned ids and statuses are returned.
/// </summary>
public class PayPalPaymentService : IPayPalPaymentService
{
    private const string TokenCacheKey = "paypal-access-token";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<PayPalPaymentService> _logger;

    public PayPalPaymentService(HttpClient http, PayPalSettings settings, IMemoryCache cache,
        IAppLogger<PayPalPaymentService> logger)
    {
        _http = http;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    private string Currency => _settings.ResolvedCurrency;

    // ---------------------------------------------------------------------------------------------
    // Authorization (hold)
    // ---------------------------------------------------------------------------------------------

    public Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card,
        string orderReference, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var cardBody = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["name"] = card.Name,
            ["billing_address"] = BuildBillingAddress(card.BillingAddress)
        };
        return CreateAuthorizationAsync(amount, currency, cardBody, orderReference, idempotencyKey, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultId,
        string orderReference, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var cardBody = new Dictionary<string, object?> { ["vault_id"] = vaultId };
        return CreateAuthorizationAsync(amount, currency, cardBody, orderReference, idempotencyKey, cancellationToken);
    }

    private async Task<PayPalAuthorizationResult> CreateAuthorizationAsync(decimal amount, string currency,
        Dictionary<string, object?> cardBody, string orderReference, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                // custom_id carries our order id so it surfaces as custom_field in reconciliation.
                // invoice_id is deliberately omitted here: some accounts enforce a globally-unique
                // invoice per transaction, and a run-unique invoice is applied at capture instead.
                new
                {
                    custom_id = orderReference,
                    amount = Money(amount, currency)
                }
            },
            payment_source = new { card = cardBody }
        };

        using var response = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, idempotencyKey,
            preferRepresentation: true, cancellationToken);
        var json = await ReadJsonAsync(response, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw MapError(response.StatusCode, json, "authorize the payment");

        var root = json.RootElement;
        var status = GetString(root, "status") ?? "";

        // A challenge that needs a browser approval is surfaced, never worked around.
        if (RequiresPayerAction(root, status))
            throw new PayPalChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (3-D Secure). " +
                "This server-to-server integration does not perform browser approval round-trips.");

        if (!TryGetAuthorization(root, out var auth))
            throw new PaymentException($"PayPal did not return an authorization (order status '{status}').");

        var authId = GetString(auth, "id")
            ?? throw new PaymentException("PayPal authorization response contained no id.");
        var authStatus = GetString(auth, "status") ?? "CREATED";
        var expiresAt = GetDateTime(auth, "expiration_time");
        var (brand, lastFour) = ReadCardSummary(root);

        return new PayPalAuthorizationResult(
            PayPalOrderId: GetString(root, "id") ?? "",
            AuthorizationId: authId,
            Status: authStatus,
            ExpiresAt: expiresAt,
            CardBrand: brand,
            CardLastFour: lastFour);
    }

    // ---------------------------------------------------------------------------------------------
    // Capture (take)
    // ---------------------------------------------------------------------------------------------

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string invoiceId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new { invoice_id = invoiceId, final_capture = true };
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", body, idempotencyKey,
            preferRepresentation: true, cancellationToken);
        var json = await ReadJsonAsync(response, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // A hold that has gone stale (past its honor period) is renewable — flag it so callers
            // can re-authorize rather than fail the fulfilment.
            if (IndicatesStaleAuthorization(response.StatusCode, json))
                throw new StaleAuthorizationException(
                    $"Authorization {authorizationId} can no longer be captured directly: {DescribeError(json)}");
            throw MapError(response.StatusCode, json, "capture the payment");
        }

        var root = json.RootElement;
        var captureId = GetString(root, "id")
            ?? throw new PaymentException("PayPal capture response contained no id.");
        var status = GetString(root, "status") ?? "COMPLETED";

        decimal gross = 0m, fee = 0m, net = 0m;
        var currency = Currency;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            gross = ReadMoney(breakdown, "gross_amount", out currency);
            fee = ReadMoney(breakdown, "paypal_fee", out _);
            net = ReadMoney(breakdown, "net_amount", out _);
        }
        else if (root.TryGetProperty("amount", out var amt))
        {
            gross = ParseValue(amt, out currency);
            net = gross;
        }

        return new PayPalCaptureResult(captureId, status, gross, fee, net, currency);
    }

    // ---------------------------------------------------------------------------------------------
    // Reauthorize / Void
    // ---------------------------------------------------------------------------------------------

    public async Task<PayPalReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken cancellationToken = default)
    {
        var body = new { amount = Money(amount, currency) };
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, requestId: null,
            preferRepresentation: true, cancellationToken);
        var json = await ReadJsonAsync(response, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new AuthorizationNotRenewableException(
                $"The authorization for this order could not be renewed and the funds cannot be captured " +
                $"({DescribeError(json)}). The shopper must place and pay for a new order.");

        var root = json.RootElement;
        var newId = GetString(root, "id")
            ?? throw new AuthorizationNotRenewableException("PayPal reauthorization returned no authorization id.");
        return new PayPalReauthorizeResult(newId, GetString(root, "status") ?? "CREATED",
            GetDateTime(root, "expiration_time"));
    }

    public async Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void", body: null, requestId: null,
            preferRepresentation: false, cancellationToken);

        if (response.IsSuccessStatusCode)
            return;

        var json = await ReadJsonAsync(response, cancellationToken);
        // Voiding something already voided/captured is effectively a no-op for our purposes.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
        {
            _logger.LogWarning("Void of authorization {AuthorizationId} returned {Status}: {Detail}",
                authorizationId, (int)response.StatusCode, DescribeError(json));
            return;
        }
        throw MapError(response.StatusCode, json, "release the authorization");
    }

    // ---------------------------------------------------------------------------------------------
    // Refund
    // ---------------------------------------------------------------------------------------------

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // invoice_id is intentionally omitted: distinct partial refunds of one capture would otherwise
        // collide under accounts that enforce unique invoice ids. Idempotency comes from the
        // PayPal-Request-Id (the caller's key), so a repeat never refunds twice.
        object body = amount.HasValue
            ? new { amount = Money(amount.Value, currency) }
            : new { };

        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund", body, idempotencyKey,
            preferRepresentation: true, cancellationToken);
        var json = await ReadJsonAsync(response, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw MapError(response.StatusCode, json, "refund the payment");

        var root = json.RootElement;
        var refundId = GetString(root, "id")
            ?? throw new PaymentException("PayPal refund response contained no id.");
        var status = GetString(root, "status") ?? "COMPLETED";

        var refunded = amount ?? 0m;
        var refundCurrency = currency;
        if (root.TryGetProperty("amount", out var amt))
            refunded = ParseValue(amt, out refundCurrency);
        else if (root.TryGetProperty("seller_payable_breakdown", out var breakdown))
            refunded = ReadMoney(breakdown, "gross_amount", out refundCurrency);

        return new PayPalRefundResult(refundId, status, refunded, refundCurrency);
    }

    // ---------------------------------------------------------------------------------------------
    // Vault (save / delete card)
    // ---------------------------------------------------------------------------------------------

    public async Task<PayPalVaultResult> VaultCardAsync(CardDetails card, string? customerId,
        CancellationToken cancellationToken = default)
    {
        // Step 1: create a setup token describing the card.
        var setupCard = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["name"] = card.Name,
            ["billing_address"] = BuildBillingAddress(card.BillingAddress)
        };
        object setupPaymentSource = customerId is null
            ? new { card = setupCard }
            : new { card = setupCard, customer = new { id = customerId } };
        var setupBody = new { payment_source = setupPaymentSource };

        using var setupResponse = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody,
            requestId: null, preferRepresentation: true, cancellationToken);
        var setupJson = await ReadJsonAsync(setupResponse, cancellationToken);
        if (!setupResponse.IsSuccessStatusCode)
            throw MapError(setupResponse.StatusCode, setupJson, "save the card");

        var setupRoot = setupJson.RootElement;
        if (RequiresPayerAction(setupRoot, GetString(setupRoot, "status") ?? ""))
            throw new PayPalChallengeRequiredException(
                "Saving this card requires browser verification, which this integration does not perform.");
        var setupTokenId = GetString(setupRoot, "id")
            ?? throw new PaymentException("PayPal setup-token response contained no id.");

        // Step 2: exchange the setup token for a durable payment token (the vault id).
        var tokenBody = new { payment_source = new { token = new { id = setupTokenId, type = "SETUP_TOKEN" } } };
        using var tokenResponse = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenBody,
            requestId: null, preferRepresentation: true, cancellationToken);
        var tokenJson = await ReadJsonAsync(tokenResponse, cancellationToken);
        if (!tokenResponse.IsSuccessStatusCode)
            throw MapError(tokenResponse.StatusCode, tokenJson, "save the card");

        var tokenRoot = tokenJson.RootElement;
        var vaultId = GetString(tokenRoot, "id")
            ?? throw new PaymentException("PayPal payment-token response contained no id.");
        string? vaultCustomerId = null;
        if (tokenRoot.TryGetProperty("customer", out var customer))
            vaultCustomerId = GetString(customer, "id");

        var (brand, lastFour) = ReadCardSummary(tokenRoot);
        string? expiry = null, holderName = null;
        if (tokenRoot.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            expiry = GetString(cardEl, "expiry");
            holderName = GetString(cardEl, "name");
        }

        return new PayPalVaultResult(vaultId, vaultCustomerId, brand, lastFour, expiry, holderName);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}",
            body: null, requestId: null, preferRepresentation: false, cancellationToken);

        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            return;

        var json = await ReadJsonAsync(response, cancellationToken);
        throw MapError(response.StatusCode, json, "remove the saved card");
    }

    // ---------------------------------------------------------------------------------------------
    // Reconciliation reporting
    // ---------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();

        // PayPal allows a maximum 31-day range per request, so cover the whole requested range in
        // ≤31-day windows and page through each window until every page is read.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            int totalPages;
            do
            {
                var query = "/v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(FormatReportDate(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatReportDate(windowEnd))}" +
                    $"&fields=all&page_size=500&page={page}";

                using var response = await SendAsync(HttpMethod.Get, query, body: null, requestId: null,
                    preferRepresentation: false, cancellationToken);
                var json = await ReadJsonAsync(response, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    throw MapError(response.StatusCode, json, "read the PayPal transaction report");

                var root = json.RootElement;
                if (root.TryGetProperty("transaction_details", out var details) &&
                    details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (!detail.TryGetProperty("transaction_info", out var info))
                            continue;
                        var amount = ReadMoney(info, "transaction_amount", out var currency);
                        results.Add(new PayPalTransaction(
                            TransactionId: GetString(info, "transaction_id") ?? "",
                            Status: GetString(info, "transaction_status") ?? "",
                            Amount: amount,
                            Currency: currency,
                            InvoiceId: GetString(info, "invoice_id"),
                            CustomField: GetString(info, "custom_field"),
                            InitiationDate: GetDateTime(info, "transaction_initiation_date")));
                    }
                }

                totalPages = root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var t) ? t : 1;
                page++;
            }
            while (page <= totalPages && !cancellationToken.IsCancellationRequested);

            windowStart = windowEnd;
        }

        return results;
    }

    // ---------------------------------------------------------------------------------------------
    // HTTP plumbing + OAuth token
    // ---------------------------------------------------------------------------------------------

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? requestId,
        bool preferRepresentation, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (requestId != null)
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        if (preferRepresentation)
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (body != null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        return await _http.SendAsync(request, cancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<string>(TokenCacheKey, out var cached) && cached != null)
            return cached;

        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
            throw new PaymentException("PayPal credentials are not configured (PayPal:ClientId / PayPal:ClientSecret).");

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            })
        };
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var response = await _http.SendAsync(request, cancellationToken);
        var json = await ReadJsonAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw MapError(response.StatusCode, json, "obtain a PayPal access token");

        var root = json.RootElement;
        var accessToken = GetString(root, "access_token")
            ?? throw new PaymentException("PayPal token response contained no access_token.");
        var expiresIn = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var secs) ? secs : 300;

        // Refresh a minute early so an in-flight call never rides an expired token.
        _cache.Set(TokenCacheKey, accessToken, TimeSpan.FromSeconds(Math.Max(30, expiresIn - 60)));
        return accessToken;
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private object Money(decimal amount, string currency) => new
    {
        currency_code = string.IsNullOrWhiteSpace(currency) ? Currency : currency,
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static Dictionary<string, object?>? BuildBillingAddress(CardBillingAddress? address)
    {
        if (address is null)
            return null;
        var result = new Dictionary<string, object?>
        {
            ["address_line_1"] = address.AddressLine1,
            ["address_line_2"] = address.AddressLine2,
            ["admin_area_2"] = address.AdminArea2,
            ["admin_area_1"] = address.AdminArea1,
            ["postal_code"] = address.PostalCode,
            ["country_code"] = address.CountryCode
        };
        return result;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
            return JsonDocument.Parse("{}");
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}");
        }
    }

    private static bool RequiresPayerAction(JsonElement root, string status)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            return true;
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                if (string.Equals(GetString(link, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private static bool TryGetAuthorization(JsonElement root, out JsonElement authorization)
    {
        authorization = default;
        if (!root.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments) &&
                payments.TryGetProperty("authorizations", out var auths) &&
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

    private static (string brand, string lastFour) ReadCardSummary(JsonElement root)
    {
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var card))
            return (GetString(card, "brand") ?? "CARD", GetString(card, "last_digits") ?? "");
        return ("CARD", "");
    }

    private static bool IndicatesStaleAuthorization(HttpStatusCode statusCode, JsonDocument json)
    {
        if (statusCode == HttpStatusCode.NotFound)
            return true; // authorization no longer exists to capture
        var text = DescribeError(json).ToUpperInvariant();
        return text.Contains("EXPIRED") || text.Contains("AUTHORIZATION_EXPIRED");
    }

    private PaymentException MapError(HttpStatusCode statusCode, JsonDocument json, string action)
    {
        var detail = DescribeError(json);
        _logger.LogWarning("PayPal call failed ({Status}) while trying to {Action}: {Detail}",
            (int)statusCode, action, detail);
        return new PaymentException($"Could not {action}. PayPal returned {(int)statusCode}: {detail}");
    }

    private static string DescribeError(JsonDocument json)
    {
        var root = json.RootElement;
        var name = GetString(root, "name") ?? GetString(root, "error");
        var message = GetString(root, "message") ?? GetString(root, "error_description");
        var issues = new List<string>();
        if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in details.EnumerateArray())
            {
                var issue = GetString(d, "issue");
                var desc = GetString(d, "description");
                if (issue != null || desc != null)
                    issues.Add($"{issue}{(desc != null ? $" - {desc}" : "")}");
            }
        }
        var parts = new List<string>();
        if (name != null) parts.Add(name);
        if (message != null) parts.Add(message);
        if (issues.Count > 0) parts.Add(string.Join("; ", issues));
        return parts.Count > 0 ? string.Join(": ", parts) : "no error detail provided";
    }

    private static decimal ReadMoney(JsonElement parent, string property, out string currency)
    {
        currency = "";
        if (parent.TryGetProperty(property, out var money))
            return ParseValue(money, out currency);
        return 0m;
    }

    private static decimal ParseValue(JsonElement money, out string currency)
    {
        currency = GetString(money, "currency_code") ?? "";
        var value = GetString(money, "value");
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? GetDateTime(JsonElement element, string property)
    {
        var raw = GetString(element, property);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
            ? dt
            : null;
    }

    private static string FormatReportDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

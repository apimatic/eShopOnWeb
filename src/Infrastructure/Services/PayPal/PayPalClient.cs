using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Typed client over the PayPal REST APIs. All PayPal knowledge lives here. Card numbers, CVVs
/// and credentials are never logged; only ids, statuses and PayPal debug ids are.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private const string AccessTokenCacheKey = "PayPal:AccessToken";

    // Currencies with no minor unit (amounts must be whole numbers).
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "HUF", "TWD"
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PayPalClient> _logger;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalSettings> settings, IMemoryCache cache, ILogger<PayPalClient> logger)
    {
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
        _httpClient = httpClient;
        if (_httpClient.BaseAddress is null)
            _httpClient.BaseAddress = new Uri(_settings.ResolveBaseUrl());
    }

    private string Currency => string.IsNullOrWhiteSpace(_settings.Currency) ? "USD" : _settings.Currency.Trim().ToUpperInvariant();

    // ---------- Orders / authorize ----------

    public async Task<AuthorizeOrderResult> CreateAuthorizedOrderAsync(AuthorizeOrderRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object cardNode = request.VaultId is not null
            ? new { vault_id = request.VaultId }
            : BuildCardNode(request.Card!);

        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    invoice_id = request.InvoiceId,
                    custom_id = request.CustomId,
                    amount = Amount(request.Amount)
                }
            },
            payment_source = new { card = cardNode }
        };

        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = idempotencyKey,
            ["Prefer"] = "return=representation"
        };

        var root = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", body, headers, cancellationToken);

        var status = (string?)root?["status"] ?? "UNKNOWN";
        var orderId = (string?)root?["id"] ?? string.Empty;

        // A browser approval / 3-D Secure challenge — we stop rather than build an approval round-trip.
        var approvalUrl = FindLink(root, "payer-action");
        if (status == "PAYER_ACTION_REQUIRED" || approvalUrl is not null)
        {
            return new AuthorizeOrderResult(orderId, status, null, null, null, request.Amount, Currency, null, null, true, approvalUrl);
        }

        var authorization = root?["purchase_units"]?[0]?["payments"]?["authorizations"]?[0];
        var cardResp = root?["payment_source"]?["card"];

        return new AuthorizeOrderResult(
            PayPalOrderId: orderId,
            OrderStatus: status,
            AuthorizationId: (string?)authorization?["id"],
            AuthorizationStatus: (string?)authorization?["status"],
            AuthorizationExpiresAt: ParseDate((string?)authorization?["expiration_time"]),
            AuthorizedAmount: request.Amount,
            Currency: (string?)authorization?["amount"]?["currency_code"] ?? Currency,
            CardBrand: (string?)cardResp?["brand"],
            CardLast4: (string?)cardResp?["last_digits"],
            RequiresApproval: false,
            ApprovalUrl: null);
    }

    // ---------- Payments ----------

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = idempotencyKey,
            ["Prefer"] = "return=representation"
        };

        var root = await SendJsonAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", new { }, headers, cancellationToken);

        var breakdown = root?["seller_receivable_breakdown"];
        var currency = (string?)breakdown?["gross_amount"]?["currency_code"]
                       ?? (string?)root?["amount"]?["currency_code"] ?? Currency;

        return new CaptureResult(
            CaptureId: (string?)root?["id"] ?? throw MalformedResponse("capture", root),
            Status: (string?)root?["status"] ?? "UNKNOWN",
            GrossAmount: ParseMoney(breakdown?["gross_amount"]) ?? ParseMoney(root?["amount"]) ?? 0m,
            PayPalFee: ParseMoney(breakdown?["paypal_fee"]) ?? 0m,
            NetAmount: ParseMoney(breakdown?["net_amount"]) ?? 0m,
            Currency: currency);
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, CancellationToken cancellationToken = default)
    {
        var headers = new Dictionary<string, string> { ["Prefer"] = "return=representation" };
        var body = new { amount = Amount(amount) };
        var root = await SendJsonAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, headers, cancellationToken);
        return new AuthorizationResult(
            (string?)root?["id"] ?? authorizationId,
            (string?)root?["status"] ?? "CREATED",
            ParseDate((string?)root?["expiration_time"]));
    }

    public async Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var root = await SendJsonAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        return new AuthorizationResult(
            (string?)root?["id"] ?? authorizationId,
            (string?)root?["status"] ?? "UNKNOWN",
            ParseDate((string?)root?["expiration_time"]));
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        await SendJsonAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, null, cancellationToken);
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = idempotencyKey,
            ["Prefer"] = "return=representation"
        };

        object body = amount.HasValue ? new { amount = Amount(amount.Value) } : new { };
        var root = await SendJsonAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, headers, cancellationToken);

        var refundedAmount = ParseMoney(root?["amount"]) ?? amount ?? 0m;
        var currency = (string?)root?["amount"]?["currency_code"] ?? Currency;
        return new RefundResult(
            (string?)root?["id"] ?? throw MalformedResponse("refund", root),
            (string?)root?["status"] ?? "UNKNOWN",
            refundedAmount,
            currency);
    }

    // ---------- Vault ----------

    public async Task<VaultCardResult> VaultCardAsync(PayPalCard card, string? customerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object body = string.IsNullOrEmpty(customerId)
            ? new { payment_source = new { card = BuildCardNode(card) } }
            : new { customer = new { id = customerId }, payment_source = new { card = BuildCardNode(card) } };

        var headers = new Dictionary<string, string> { ["PayPal-Request-Id"] = idempotencyKey };
        var root = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, headers, cancellationToken);

        var cardResp = root?["payment_source"]?["card"];
        return new VaultCardResult(
            VaultTokenId: (string?)root?["id"] ?? throw MalformedResponse("vault token", root),
            CustomerId: (string?)root?["customer"]?["id"] ?? customerId ?? string.Empty,
            Brand: (string?)cardResp?["brand"],
            Last4: (string?)cardResp?["last_digits"],
            Expiry: (string?)cardResp?["expiry"]);
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await SendJsonAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultTokenId}", null, null, cancellationToken);
    }

    // ---------- Transaction search / reconciliation ----------

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, PayPalTransaction>(StringComparer.Ordinal);

        // Transaction Search allows at most a ~31-day window per request, so chunk the range.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            var totalPages = 1;
            do
            {
                var query = $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(FormatRfc3339(windowStart))}" +
                            $"&end_date={Uri.EscapeDataString(FormatRfc3339(windowEnd))}" +
                            $"&fields=transaction_info&page_size=100&page={page}";

                var root = await SendJsonAsync(HttpMethod.Get, query, null, null, cancellationToken);
                totalPages = (int?)root?["total_pages"] ?? 1;

                foreach (var detail in root?["transaction_details"]?.AsArray() ?? new JsonArray())
                {
                    var info = detail?["transaction_info"];
                    var id = (string?)info?["transaction_id"];
                    if (string.IsNullOrEmpty(id)) continue;

                    results[id] = new PayPalTransaction(
                        TransactionId: id,
                        InvoiceId: (string?)info?["invoice_id"],
                        CustomField: (string?)info?["custom_field"],
                        Amount: ParseMoney(info?["transaction_amount"]) ?? 0m,
                        Currency: (string?)info?["transaction_amount"]?["currency_code"] ?? Currency,
                        Status: (string?)info?["transaction_status"] ?? "UNKNOWN",
                        InitiationDate: ParseDate((string?)info?["transaction_initiation_date"]));
                }

                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd;
        }

        return results.Values.OrderBy(t => t.InitiationDate ?? DateTimeOffset.MinValue).ToList();
    }

    // ---------- request plumbing ----------

    private object BuildCardNode(PayPalCard card)
    {
        object? billing = card.BillingAddress is null ? null : new
        {
            address_line_1 = card.BillingAddress.AddressLine1,
            address_line_2 = card.BillingAddress.AddressLine2,
            admin_area_2 = card.BillingAddress.AdminArea2,
            admin_area_1 = card.BillingAddress.AdminArea1,
            postal_code = card.BillingAddress.PostalCode,
            country_code = card.BillingAddress.CountryCode
        };

        return new
        {
            name = card.Name,
            number = card.Number,
            expiry = card.Expiry,
            security_code = card.SecurityCode,
            billing_address = billing
        };
    }

    private object Amount(decimal value) => new
    {
        currency_code = Currency,
        value = FormatAmount(value)
    };

    private string FormatAmount(decimal value)
    {
        var decimals = ZeroDecimalCurrencies.Contains(Currency) ? 0 : 2;
        return Math.Round(value, decimals, MidpointRounding.AwayFromZero).ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    private async Task<JsonNode?> SendJsonAsync(HttpMethod method, string path, object? body, IDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var httpRequest = new HttpRequestMessage(method, path);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (headers is not null)
        {
            foreach (var (key, val) in headers)
                httpRequest.Headers.TryAddWithoutValidation(key, val);
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, SerializerOptions);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw BuildApiException(response.StatusCode, payload, method, path);

        return string.IsNullOrWhiteSpace(payload) ? null : JsonNode.Parse(payload);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(AccessTokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
            return cached!;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw BuildApiException(response.StatusCode, payload, HttpMethod.Post, "/v1/oauth2/token");

        var root = JsonNode.Parse(payload);
        var token = (string?)root?["access_token"] ?? throw new PayPalApiException((int)response.StatusCode, "invalid_token_response", "PayPal did not return an access token.", null);
        var expiresIn = (int?)root?["expires_in"] ?? 3000;

        // Cache slightly short of the real expiry to avoid using a token at the boundary.
        _cache.Set(AccessTokenCacheKey, token, TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60)));
        return token;
    }

    private PayPalApiException BuildApiException(HttpStatusCode statusCode, string payload, HttpMethod method, string path)
    {
        string? name = null, message = null, debugId = null;
        var details = new List<string>();
        try
        {
            var root = JsonNode.Parse(payload);
            name = (string?)root?["name"] ?? (string?)root?["error"];
            message = (string?)root?["message"] ?? (string?)root?["error_description"];
            debugId = (string?)root?["debug_id"];
            foreach (var d in root?["details"]?.AsArray() ?? new JsonArray())
            {
                var issue = (string?)d?["issue"];
                var desc = (string?)d?["description"];
                if (issue is not null || desc is not null)
                    details.Add($"{issue}: {desc}".Trim(':', ' '));
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall back to status only.
        }

        message ??= $"PayPal request failed with status {(int)statusCode}.";
        _logger.LogWarning("PayPal API error on {Method} {Path}: status={Status} name={Name} debug_id={DebugId} details={Details}",
            method, path, (int)statusCode, name, debugId, string.Join("; ", details));

        return new PayPalApiException((int)statusCode, name, message, debugId, details);
    }

    private static PayPalApiException MalformedResponse(string what, JsonNode? root) =>
        new(502, "malformed_response", $"PayPal returned a {what} response without the expected fields.", null);

    private static string? FindLink(JsonNode? root, string rel)
    {
        foreach (var link in root?["links"]?.AsArray() ?? new JsonArray())
        {
            if (string.Equals((string?)link?["rel"], rel, StringComparison.OrdinalIgnoreCase))
                return (string?)link?["href"];
        }
        return null;
    }

    private static decimal? ParseMoney(JsonNode? money)
    {
        var value = (string?)money?["value"];
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;

    private static string FormatRfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}

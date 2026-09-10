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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal REST integration: Orders v2 (authorize), Payments v2 (capture/void/reauthorize/refund),
/// Vault v3 (save/delete cards) and Transaction Search v1 (reconciliation). All money-moving
/// writes carry a <c>PayPal-Request-Id</c> for idempotency.
/// </summary>
public class PayPalClient : IPayPalPaymentGateway
{
    private const string AccessTokenCacheKey = "paypal:access-token";
    private const int MaxReportPageSize = 500;
    private static readonly TimeSpan ReportWindow = TimeSpan.FromDays(30); // stay under PayPal's 31-day limit

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<PayPalClient> _logger;
    private readonly string _baseUrl;

    public PayPalClient(HttpClient http, IOptions<PayPalSettings> options, IMemoryCache cache,
        IAppLogger<PayPalClient> logger)
    {
        _http = http;
        _settings = options.Value;
        _cache = cache;
        _logger = logger;
        _baseUrl = ResolveBaseUrl(_settings);
    }

    private static string ResolveBaseUrl(PayPalSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return settings.BaseUrl.TrimEnd('/');
        }
        var env = settings.Environment?.Trim().ToLowerInvariant();
        return env is "live" or "production"
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }

    // ---- authorize ----------------------------------------------------------

    public async Task<AuthorizeResult> AuthorizeAsync(AuthorizeRequest request, CancellationToken cancellationToken = default)
    {
        object paymentSourceCard = request.VaultId is not null
            ? new Dictionary<string, object?> { ["vault_id"] = request.VaultId }
            : BuildCard(request.Card!);

        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["invoice_id"] = request.InvoiceId,
                    ["amount"] = Amount(request.Amount)
                }
            },
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = paymentSourceCard }
        };

        using var doc = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", body,
            request.IdempotencyKey, cancellationToken);
        var root = doc!.RootElement;

        var orderId = GetString(root, "id")
            ?? throw new PayPalApiException("PayPal did not return an order id for the authorization.");

        var authorization = root
            .TryGetProperty("purchase_units", out var pus) && pus.GetArrayLength() > 0 &&
            pus[0].TryGetProperty("payments", out var payments) &&
            payments.TryGetProperty("authorizations", out var auths) && auths.GetArrayLength() > 0
                ? auths[0]
                : (JsonElement?)null;

        if (authorization is null)
        {
            var status = GetString(root, "status");
            if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
                HasLink(root, "payer-action"))
            {
                throw new PayPalApiException(
                    "PayPal requires the shopper to approve this card payment in a browser (a challenge/3-D Secure step). " +
                    "This integration performs no browser approval round-trip.")
                { PayPalIssue = "PAYER_ACTION_REQUIRED" };
            }
            throw new PayPalApiException($"PayPal returned no authorization for order {orderId} (status {status}).");
        }

        var auth = authorization.Value;
        return new AuthorizeResult(
            PayPalOrderId: orderId,
            AuthorizationId: GetString(auth, "id") ?? throw new PayPalApiException("Missing authorization id."),
            Status: GetString(auth, "status") ?? "CREATED",
            ExpiresAt: GetDate(auth, "expiration_time"));
    }

    public async Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        var root = doc!.RootElement;
        return new AuthorizationDetails(GetString(root, "status") ?? "UNKNOWN", GetDate(root, "expiration_time"));
    }

    // ---- capture ------------------------------------------------------------

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string invoiceId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = Amount(amount),
            ["final_capture"] = true,
            ["invoice_id"] = invoiceId
        };

        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", body, idempotencyKey, cancellationToken,
            preferRepresentation: true);
        var root = doc!.RootElement;

        var captureId = GetString(root, "id") ?? throw new PayPalApiException("PayPal did not return a capture id.");
        var status = GetString(root, "status") ?? "COMPLETED";

        if (TryGetBreakdown(root, out var gross, out var fee, out var net))
        {
            return new CaptureResult(captureId, status, gross, fee, net);
        }

        // Fall back to fetching the capture if the representation lacked the fee breakdown.
        using var details = await SendJsonAsync(HttpMethod.Get, $"/v2/payments/captures/{captureId}", null, null, cancellationToken);
        TryGetBreakdown(details!.RootElement, out gross, out fee, out net);
        var capturedGross = gross == 0m ? amount : gross;
        return new CaptureResult(captureId, GetString(details.RootElement, "status") ?? status, capturedGross, fee, net == 0m ? capturedGross - fee : net);
    }

    public async Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["amount"] = Amount(amount) };
        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, idempotencyKey, cancellationToken);
        var root = doc!.RootElement;
        return new ReauthorizeResult(
            AuthorizationId: GetString(root, "id") ?? authorizationId,
            Status: GetString(root, "status") ?? "CREATED",
            ExpiresAt: GetDate(root, "expiration_time"));
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void", null, null, cancellationToken);
    }

    // ---- refund -------------------------------------------------------------

    public async Task<RefundResult> RefundAsync(string captureId, decimal amount, string invoiceId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = Amount(amount),
            ["invoice_id"] = invoiceId
        };
        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund", body, idempotencyKey, cancellationToken);
        var root = doc!.RootElement;

        var refundedAmount = amount;
        if (root.TryGetProperty("amount", out var amt) && TryGetDecimal(amt, "value", out var parsed))
        {
            refundedAmount = parsed;
        }
        return new RefundResult(
            PayPalRefundId: GetString(root, "id") ?? throw new PayPalApiException("PayPal did not return a refund id."),
            Status: GetString(root, "status") ?? "COMPLETED",
            Amount: refundedAmount);
    }

    // ---- vault --------------------------------------------------------------

    public async Task<VaultCardResult> VaultCardAsync(VaultCardRequest request, CancellationToken cancellationToken = default)
    {
        // Step 1: setup token holding the raw card.
        var setupBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = BuildCard(request.Card) }
        };
        if (!string.IsNullOrWhiteSpace(request.CustomerId))
        {
            setupBody["customer"] = new Dictionary<string, object?> { ["id"] = request.CustomerId };
        }

        using var setupDoc = await SendJsonAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody,
            Guid.NewGuid().ToString("N"), cancellationToken);
        var setupTokenId = GetString(setupDoc!.RootElement, "id")
            ?? throw new PayPalApiException("PayPal did not return a setup token id.");

        // Step 2: exchange the setup token for a permanent payment token.
        var tokenBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?> { ["id"] = setupTokenId, ["type"] = "SETUP_TOKEN" }
            }
        };
        using var tokenDoc = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenBody,
            Guid.NewGuid().ToString("N"), cancellationToken);
        var root = tokenDoc!.RootElement;

        var tokenId = GetString(root, "id") ?? throw new PayPalApiException("PayPal did not return a payment token id.");
        var customerId = root.TryGetProperty("customer", out var cust) ? GetString(cust, "id") ?? "" : "";
        var card = root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var c)
            ? c
            : (JsonElement?)null;

        return new VaultCardResult(
            TokenId: tokenId,
            CustomerId: customerId,
            Brand: card is not null ? GetString(card.Value, "brand") ?? "" : "",
            LastFourDigits: card is not null ? GetString(card.Value, "last_digits") ?? "" : "",
            Expiry: card is not null ? GetString(card.Value, "expiry") ?? request.Card.Expiry : request.Card.Expiry,
            CardholderName: card is not null ? GetString(card.Value, "name") ?? request.Card.CardholderName : request.Card.CardholderName);
    }

    public async Task DeleteVaultedCardAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var message = new HttpRequestMessage(HttpMethod.Delete, $"{_baseUrl}/v3/vault/payment-tokens/{tokenId}");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(message, cancellationToken);

        // A missing token is an acceptable outcome for a delete.
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        throw ToApiException("DELETE", "/v3/vault/payment-tokens", response.StatusCode, payload);
    }

    // ---- reconciliation -----------------------------------------------------

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var transactions = new List<PayPalTransaction>();
        var windowStart = from;

        while (windowStart < to)
        {
            var windowEnd = windowStart + ReportWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            var page = 1;
            var totalPages = 1;
            do
            {
                var query =
                    $"?start_date={Uri.EscapeDataString(FormatReportDate(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatReportDate(windowEnd))}" +
                    $"&fields=all&page_size={MaxReportPageSize}&page={page}";
                using var doc = await SendJsonAsync(HttpMethod.Get, $"/v1/reporting/transactions{query}", null, null, cancellationToken);
                var root = doc!.RootElement;

                if (root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var parsedTp))
                {
                    totalPages = parsedTp;
                }

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (!detail.TryGetProperty("transaction_info", out var info))
                        {
                            continue;
                        }
                        var amount = 0m; var currency = _settings.Currency;
                        if (info.TryGetProperty("transaction_amount", out var ta))
                        {
                            TryGetDecimal(ta, "value", out amount);
                            currency = GetString(ta, "currency_code") ?? currency;
                        }
                        decimal? fee = null;
                        if (info.TryGetProperty("fee_amount", out var fa) && TryGetDecimal(fa, "value", out var feeVal))
                        {
                            fee = feeVal;
                        }
                        transactions.Add(new PayPalTransaction(
                            TransactionId: GetString(info, "transaction_id") ?? "",
                            Status: GetString(info, "transaction_status") ?? "",
                            Amount: amount,
                            CurrencyCode: currency,
                            Fee: fee,
                            InvoiceId: GetString(info, "invoice_id"),
                            Date: GetDate(info, "transaction_initiation_date")));
                    }
                }
                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd == windowStart ? to : windowEnd;
        }

        return transactions;
    }

    // ---- HTTP plumbing ------------------------------------------------------

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(AccessTokenCacheKey, out string? cached) && cached is not null)
        {
            return cached;
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/oauth2/token");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        message.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        using var response = await _http.SendAsync(message, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToApiException("POST", "/v1/oauth2/token", response.StatusCode, payload);
        }

        using var doc = JsonDocument.Parse(payload);
        var token = GetString(doc.RootElement, "access_token")
            ?? throw new PayPalApiException("PayPal did not return an access token.");
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var secs) ? secs : 300;
        _cache.Set(AccessTokenCacheKey, token, TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60)));
        return token;
    }

    private async Task<JsonDocument?> SendJsonAsync(HttpMethod method, string path, object? body,
        string? idempotencyKey, CancellationToken cancellationToken, bool preferRepresentation = false)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var message = new HttpRequestMessage(method, $"{_baseUrl}{path}");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            message.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (preferRepresentation)
        {
            message.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(message, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ToApiException(method.Method, path, response.StatusCode, payload);
        }
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }
        return JsonDocument.Parse(payload);
    }

    private PayPalApiException ToApiException(string method, string path, HttpStatusCode status, string payload)
    {
        string? name = null;
        var issues = new List<string>();
        var message = payload;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            name = GetString(root, "name");
            message = GetString(root, "message") ?? payload;
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    var issue = GetString(detail, "issue");
                    if (issue is not null) issues.Add(issue);
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the raw payload as the message.
        }

        var issueTag = string.Join(";", new[] { name }.Concat(issues).Where(s => !string.IsNullOrEmpty(s)));
        _logger.LogWarning($"PayPal {method} {path} failed ({(int)status}): {issueTag} {message}");
        return new PayPalApiException($"PayPal {method} {path} failed ({(int)status}): {message}")
        {
            PayPalIssue = string.IsNullOrEmpty(issueTag) ? null : issueTag
        };
    }

    // ---- JSON helpers -------------------------------------------------------

    private Dictionary<string, object?> Amount(decimal value) => new()
    {
        ["currency_code"] = _settings.Currency,
        ["value"] = value.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static Dictionary<string, object?> BuildCard(PaymentCard card)
    {
        var dict = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["name"] = card.CardholderName
        };
        if (!string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            dict["security_code"] = card.SecurityCode;
        }
        if (card.BillingAddress is not null)
        {
            var a = card.BillingAddress;
            var addr = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(a.Line1)) addr["address_line_1"] = a.Line1;
            if (!string.IsNullOrWhiteSpace(a.Line2)) addr["address_line_2"] = a.Line2;
            if (!string.IsNullOrWhiteSpace(a.City)) addr["admin_area_2"] = a.City;
            if (!string.IsNullOrWhiteSpace(a.State)) addr["admin_area_1"] = a.State;
            if (!string.IsNullOrWhiteSpace(a.PostalCode)) addr["postal_code"] = a.PostalCode;
            if (!string.IsNullOrWhiteSpace(a.CountryCode)) addr["country_code"] = a.CountryCode;
            if (addr.Count > 0) dict["billing_address"] = addr;
        }
        return dict;
    }

    private static bool TryGetBreakdown(JsonElement root, out decimal gross, out decimal fee, out decimal net)
    {
        gross = fee = net = 0m;
        if (!root.TryGetProperty("seller_receivable_breakdown", out var b))
        {
            return false;
        }
        var ok = false;
        if (b.TryGetProperty("gross_amount", out var g)) ok |= TryGetDecimal(g, "value", out gross);
        if (b.TryGetProperty("paypal_fee", out var f)) TryGetDecimal(f, "value", out fee);
        if (b.TryGetProperty("net_amount", out var n)) TryGetDecimal(n, "value", out net);
        return ok;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetDecimal(JsonElement element, string property, out decimal result)
    {
        result = 0m;
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            result = parsed;
            return true;
        }
        return false;
    }

    private static DateTimeOffset? GetDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static bool HasLink(JsonElement root, string rel) =>
        root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array &&
        links.EnumerateArray().Any(l => string.Equals(GetString(l, "rel"), rel, StringComparison.OrdinalIgnoreCase));

    private static string FormatReportDate(DateTimeOffset date) =>
        date.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}

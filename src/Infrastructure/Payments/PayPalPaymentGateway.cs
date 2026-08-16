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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal REST integration implementing <see cref="IPaymentGateway"/>. Talks to the PayPal
/// Orders v2, Payments v2, Vault v3, and Transaction Search (reporting) APIs directly over HTTPS.
///
/// Grounded in the PayPal plugin's guidance: OAuth 2.0 client-credentials with a cached token,
/// a PayPal-Request-Id on every POST for idempotency, Orders v2 authorize/capture split, and the
/// reporting API for reconciliation. The API base address honours the optional PayPal:BaseUrl
/// override verbatim (including the token request), otherwise it is derived from PayPal:Environment.
/// No card number is ever persisted or logged by this class.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private const string TokenCacheKey = "paypal:access_token";
    private static readonly TimeSpan MaxReportingWindow = TimeSpan.FromDays(31);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PayPalPaymentGateway> _logger;
    private readonly string _baseUrl;

    public PayPalPaymentGateway(HttpClient httpClient, PayPalSettings settings, IMemoryCache cache,
        ILogger<PayPalPaymentGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _cache = cache;
        _logger = logger;
        _baseUrl = settings.ResolveBaseUrl();
    }

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizeRequest request, CancellationToken cancellationToken = default)
    {
        object cardSource = request.VaultId != null
            ? new { vault_id = request.VaultId }
            : BuildInlineCard(request.Card!);

        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    custom_id = request.CustomId,
                    invoice_id = request.InvoiceId,
                    amount = new { currency_code = request.CurrencyCode, value = Format(request.Amount) }
                }
            },
            payment_source = new { card = cardSource }
        };

        using var doc = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", body,
            requestId: request.RequestId, preferRepresentation: true, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        EnsureNoBrowserChallenge(root);

        var payPalOrderId = GetString(root, "id") ?? throw new PaymentGatewayException("PayPal did not return an order id.");
        var status = GetString(root, "status") ?? "UNKNOWN";

        var authorization = FindFirstAuthorization(root)
            ?? throw new PaymentGatewayException($"PayPal order {payPalOrderId} did not return an authorization (status {status}).");

        var authId = GetString(authorization, "id")
            ?? throw new PaymentGatewayException("PayPal returned an authorization without an id.");
        var authStatus = GetString(authorization, "status") ?? "CREATED";

        var (brand, last4) = ReadCardMetadata(root);

        _logger.LogInformation("PayPal authorized order {OrderId}: authorization {AuthId} status {Status}.",
            payPalOrderId, authId, authStatus);

        return new AuthorizationResult(payPalOrderId, authId, authStatus, request.Amount, request.CurrencyCode, brand, last4);
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currencyCode,
        string requestId, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = new { currency_code = currencyCode, value = Format(amount) },
            final_capture = true
        };

        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", body,
            requestId: requestId, preferRepresentation: true, cancellationToken: cancellationToken);

        return ReadCapture(doc.RootElement, amount, currencyCode);
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode,
        CancellationToken cancellationToken = default)
    {
        var body = new { amount = new { currency_code = currencyCode, value = Format(amount) } };

        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", body,
            requestId: Guid.NewGuid().ToString(), preferRepresentation: true, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        var newAuthId = GetString(root, "id")
            ?? throw new PaymentGatewayException("PayPal reauthorization did not return an authorization id.");
        var status = GetString(root, "status") ?? "CREATED";

        _logger.LogInformation("PayPal reauthorized {OldAuth} -> {NewAuth} status {Status}.",
            authorizationId, newAuthId, status);

        return new AuthorizationResult(string.Empty, newAuthId, status, amount, currencyCode, null, null);
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void", body: null,
            requestId: requestId, preferRepresentation: false, cancellationToken: cancellationToken);
        _logger.LogInformation("PayPal voided authorization {AuthId}.", authorizationId);
    }

    public async Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, $"/v2/payments/captures/{captureId}", body: null,
            requestId: null, preferRepresentation: false, cancellationToken: cancellationToken);
        return ReadCapture(doc.RootElement, fallbackAmount: 0m, fallbackCurrency: _settings.Currency);
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode,
        string requestId, string? invoiceId, CancellationToken cancellationToken = default)
    {
        object? body = amount.HasValue
            ? new { amount = new { currency_code = currencyCode, value = Format(amount.Value) }, invoice_id = invoiceId }
            : null;

        using var doc = await SendJsonAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body,
            requestId: requestId, preferRepresentation: true, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        var refundId = GetString(root, "id") ?? throw new PaymentGatewayException("PayPal refund did not return an id.");
        var status = GetString(root, "status") ?? "UNKNOWN";
        var refundedAmount = ReadAmountValue(root, "amount") ?? amount ?? 0m;

        _logger.LogInformation("PayPal refunded capture {CaptureId}: refund {RefundId} status {Status} amount {Amount}.",
            captureId, refundId, status, Format(refundedAmount));

        return new RefundResult(refundId, status, refundedAmount, currencyCode);
    }

    public async Task<VaultCardResult> VaultCardAsync(CardDetails card, string? customerId, CancellationToken cancellationToken = default)
    {
        object body = customerId != null
            ? new { customer = new { id = customerId }, payment_source = new { card = BuildInlineCard(card) } }
            : new { payment_source = new { card = BuildInlineCard(card) } };

        using var doc = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body,
            requestId: Guid.NewGuid().ToString(), preferRepresentation: false, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        var vaultId = GetString(root, "id") ?? throw new PaymentGatewayException("PayPal vault did not return a token id.");
        string? vaultCustomerId = null;
        if (root.TryGetProperty("customer", out var customer))
        {
            vaultCustomerId = GetString(customer, "id");
        }

        string brand = "CARD", last4 = string.Empty, expiry = string.Empty;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            brand = GetString(cardEl, "brand") ?? brand;
            last4 = GetString(cardEl, "last_digits") ?? last4;
            expiry = GetString(cardEl, "expiry") ?? expiry;
        }

        _logger.LogInformation("PayPal vaulted a {Brand} card ending {Last4} as token {VaultId}.", brand, last4, vaultId);
        return new VaultCardResult(vaultId, vaultCustomerId, brand, last4, string.IsNullOrEmpty(expiry) ? null : expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", body: null,
            requestId: null, preferRepresentation: false, cancellationToken: cancellationToken);
        _logger.LogInformation("PayPal deleted vaulted card token {VaultId}.", vaultId);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();

        // PayPal reporting only accepts a 31-day window and pages results; walk the whole range.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxReportingWindow;
            if (windowEnd > to) windowEnd = to;

            try
            {
                var page = 1;
                int totalPages;
                do
                {
                    var query = $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(FormatReportingDate(windowStart))}" +
                                $"&end_date={Uri.EscapeDataString(FormatReportingDate(windowEnd))}" +
                                $"&fields=transaction_info&page_size=500&page={page}";

                    using var doc = await SendJsonAsync(HttpMethod.Get, query, body: null, requestId: null,
                        preferRepresentation: false, cancellationToken: cancellationToken);
                    var root = doc.RootElement;

                    totalPages = root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var tpv) ? tpv : 1;

                    if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var detail in details.EnumerateArray())
                        {
                            if (detail.TryGetProperty("transaction_info", out var info))
                            {
                                results.Add(ReadTransaction(info));
                            }
                        }
                    }

                    page++;
                }
                while (page <= totalPages);
            }
            catch (PaymentGatewayException ex) when (IsReportingDataUnavailable(ex))
            {
                // PayPal reporting lags live activity by a few hours; a window newer than that has no
                // data yet. That is an expected empty result, not a failure — skip the window.
                _logger.LogWarning("PayPal reporting has no data yet for {Start:o}..{End:o} ({Message}); treating window as empty.",
                    windowStart, windowEnd, ex.Message);
            }

            // Nudge past the window end to avoid re-fetching the boundary second.
            windowStart = windowEnd.AddSeconds(1);
        }

        _logger.LogInformation("PayPal reporting returned {Count} transaction(s) for {From:o}..{To:o}.",
            results.Count, from, to);
        return results;
    }

    // ---------- helpers ----------

    private object BuildInlineCard(CardDetails card)
    {
        object? billingAddress = null;
        if (card.BillingAddress is { } addr)
        {
            billingAddress = new
            {
                address_line_1 = addr.AddressLine1,
                address_line_2 = addr.AddressLine2,
                admin_area_2 = addr.AdminArea2,
                admin_area_1 = addr.AdminArea1,
                postal_code = addr.PostalCode,
                country_code = addr.CountryCode
            };
        }

        return new
        {
            number = card.Number,
            expiry = card.Expiry,
            security_code = card.SecurityCode,
            name = card.Name,
            billing_address = billingAddress
        };
    }

    private CaptureResult ReadCapture(JsonElement root, decimal fallbackAmount, string fallbackCurrency)
    {
        var captureId = GetString(root, "id") ?? throw new PaymentGatewayException("PayPal capture response had no id.");
        var status = GetString(root, "status") ?? "UNKNOWN";
        var amount = ReadAmountValue(root, "amount") ?? fallbackAmount;
        var currency = ReadAmountCurrency(root, "amount") ?? fallbackCurrency;

        decimal? fee = null, net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = ReadAmountValue(breakdown, "paypal_fee");
            net = ReadAmountValue(breakdown, "net_amount");
            var gross = ReadAmountValue(breakdown, "gross_amount");
            if (gross.HasValue) amount = gross.Value;
        }

        return new CaptureResult(captureId, status, amount, fee, net, currency);
    }

    private static PayPalTransaction ReadTransaction(JsonElement info)
    {
        var id = GetString(info, "transaction_id") ?? string.Empty;
        var reference = GetString(info, "paypal_reference_id");
        var status = GetString(info, "transaction_status");
        var amount = ReadAmountValue(info, "transaction_amount") ?? 0m;
        var currency = ReadAmountCurrency(info, "transaction_amount") ?? string.Empty;
        var fee = ReadAmountValue(info, "fee_amount");
        var custom = GetString(info, "custom_field");
        var invoice = GetString(info, "invoice_id");
        var eventCode = GetString(info, "transaction_event_code");

        DateTimeOffset? date = null;
        var dateStr = GetString(info, "transaction_initiation_date");
        if (dateStr != null && DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed))
        {
            date = parsed;
        }

        return new PayPalTransaction(id, reference, status, amount, fee, currency, custom, invoice, eventCode, date);
    }

    private static JsonElement? FindFirstAuthorization(JsonElement orderRoot)
    {
        if (!orderRoot.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments)
                && payments.TryGetProperty("authorizations", out var auths)
                && auths.ValueKind == JsonValueKind.Array
                && auths.GetArrayLength() > 0)
            {
                return auths[0];
            }
        }
        return null;
    }

    private (string? brand, string? last4) ReadCardMetadata(JsonElement orderRoot)
    {
        if (orderRoot.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var card))
        {
            return (GetString(card, "brand"), GetString(card, "last_digits"));
        }
        return (null, null);
    }

    /// <summary>
    /// If PayPal answers with a step-up that needs the shopper to approve in a browser, stop rather
    /// than building an approval round-trip.
    /// </summary>
    private static void EnsureNoBrowserChallenge(JsonElement orderRoot)
    {
        var status = GetString(orderRoot, "status");
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (e.g. a 3-D Secure challenge). " +
                "This headless card integration does not perform a browser approval round-trip.");
        }

        if (orderRoot.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                var rel = GetString(link, "rel");
                if (string.Equals(rel, "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    throw new PaymentChallengeRequiredException(
                        "PayPal returned a 'payer-action' link, indicating the shopper must approve this card payment in a browser. " +
                        "This headless card integration does not perform a browser approval round-trip.");
                }
            }
        }
    }

    // ---------- HTTP plumbing ----------

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string pathOrUrl, object? body,
        string? requestId, bool preferRepresentation, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            var token = await GetAccessTokenAsync(cancellationToken);

            using var httpRequest = new HttpRequestMessage(method, AbsoluteUrl(pathOrUrl));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (!string.IsNullOrEmpty(requestId))
            {
                httpRequest.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }
            if (preferRepresentation)
            {
                httpRequest.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            }
            if (body != null)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxAttempts)
            {
                var delay = RetryDelay(response, attempt);
                _logger.LogWarning("PayPal rate limited ({Status}); retrying in {Delay}.", (int)response.StatusCode, delay);
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw BuildGatewayException(response.StatusCode, content);
            }

            return string.IsNullOrWhiteSpace(content)
                ? JsonDocument.Parse("{}")
                : JsonDocument.Parse(content);
        }
    }

    /// <summary>
    /// True when PayPal reports there is no transaction data yet for the requested reporting window
    /// (the reporting pipeline lags live activity). Such a window is treated as empty, not an error.
    /// </summary>
    private static bool IsReportingDataUnavailable(PaymentGatewayException ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.IndexOf("not available", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("no data", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private string AbsoluteUrl(string pathOrUrl) =>
        pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? pathOrUrl : _baseUrl + pathOrUrl;

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values)
            && int.TryParse(values.FirstOrDefault(), out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(Math.Min(seconds, 30));
        }
        return TimeSpan.FromSeconds(Math.Pow(2, attempt)); // 2s, 4s, ...
    }

    private static PaymentGatewayException BuildGatewayException(HttpStatusCode statusCode, string content)
    {
        string message = $"PayPal request failed ({(int)statusCode}).";
        string? debugId = null;
        string? name = null;

        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                name = GetString(root, "name");
                debugId = GetString(root, "debug_id");
                var baseMessage = GetString(root, "message");

                string? issue = null;
                if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
                    && details.GetArrayLength() > 0)
                {
                    var first = details[0];
                    issue = GetString(first, "issue");
                    var description = GetString(first, "description");
                    if (issue != null || description != null)
                    {
                        baseMessage = $"{baseMessage} [{issue}: {description}]";
                    }
                }

                if (!string.IsNullOrEmpty(baseMessage))
                {
                    message = $"PayPal error ({(int)statusCode}) {name}: {baseMessage}";
                }
                // Prefer the specific issue for the machine-readable name so stale-auth detection works.
                if (issue != null) name = issue;
            }
            catch (JsonException)
            {
                message = $"PayPal request failed ({(int)statusCode}): {content}";
            }
        }

        return new PaymentGatewayException(message, debugId, name);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<string>(TokenCacheKey, out var cached) && !string.IsNullOrEmpty(cached))
        {
            return cached!;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/oauth2/token");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildGatewayException(response.StatusCode, content);
        }

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        var token = GetString(root, "access_token")
            ?? throw new PaymentGatewayException("PayPal token response contained no access_token.");
        var expiresIn = root.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var seconds) ? seconds : 300;

        // Refresh a minute early so we never present an expired token.
        var lifetime = TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60));
        _cache.Set(TokenCacheKey, token, lifetime);
        return token;
    }

    // ---------- JSON readers ----------

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? ReadAmountValue(JsonElement parent, string amountProperty)
    {
        if (parent.TryGetProperty(amountProperty, out var amount)
            && amount.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    private static string? ReadAmountCurrency(JsonElement parent, string amountProperty)
    {
        if (parent.TryGetProperty(amountProperty, out var amount))
        {
            return GetString(amount, "currency_code");
        }
        return null;
    }

    private static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatReportingDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}

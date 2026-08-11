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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal REST gateway built directly against the OpenAPI specs in <c>api-specs/</c>:
/// Checkout Orders v2 (authorize), Payments v2 (capture/void/reauthorize/refund), Vault v3
/// (saved cards) and Transaction Search v1 (reconciliation). Handles OAuth2 client-credentials
/// token acquisition (<c>/v1/oauth2/token</c>), idempotency headers, amount formatting,
/// pagination and error mapping. No third-party PayPal SDK is used.
/// </summary>
public sealed class PayPalClient : IPayPalGateway
{
    public const string HttpClientName = "PayPal";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalClient> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    // PayPal Transaction Search allows a maximum 31-day window per query.
    private static readonly TimeSpan MaxReportWindow = TimeSpan.FromDays(31);

    public PayPalClient(IHttpClientFactory httpClientFactory, PayPalOptions options, ILogger<PayPalClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    // ---------------------------------------------------------------- Authorize (Checkout Orders v2)

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizeRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new CreateOrderModel
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new[]
            {
                new PurchaseUnitModel
                {
                    InvoiceId = request.InvoiceId,
                    CustomId = request.CustomId,
                    Description = request.Description,
                    Amount = Money(request.Amount, request.CurrencyCode)
                }
            },
            PaymentSource = new PaymentSourceModel { Card = BuildCard(request.Card, request.VaultId) }
        };

        using var response = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, idempotencyKey, cancellationToken);
        var order = await ReadJsonAsync(response, cancellationToken);

        GuardAgainstChallenge(order);

        // With a card supplied and Prefer: return=representation, the order is processed inline and
        // the authorization is present. If PayPal instead returns an APPROVED order, authorize it.
        if (!TryReadAuthorization(order, out var auth))
        {
            var status = GetString(order, "status");
            var orderId = GetString(order, "id") ?? throw Unexpected("Create order response had no id.");
            if (!string.Equals(status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                throw Unexpected($"Order {orderId} is in status '{status}' with no authorization to act on.");
            }

            using var authResponse = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{orderId}/authorize",
                new { }, idempotencyKey + "-auth", cancellationToken);
            var authorized = await ReadJsonAsync(authResponse, cancellationToken);
            GuardAgainstChallenge(authorized);
            if (!TryReadAuthorization(authorized, out auth))
            {
                throw Unexpected($"Order {orderId} could not be authorized.");
            }
        }

        return auth!;
    }

    // ---------------------------------------------------------------- Capture (Payments v2)

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new { final_capture = true }, idempotencyKey, cancellationToken);
        var capture = await ReadJsonAsync(response, cancellationToken);

        var (amount, currency) = ReadMoney(capture, "amount");
        decimal? fee = null, net = null;
        if (capture.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            if (breakdown.TryGetProperty("gross_amount", out _))
            {
                (amount, currency) = ReadMoney(breakdown, "gross_amount");
            }
            fee = TryReadMoneyValue(breakdown, "paypal_fee");
            net = TryReadMoneyValue(breakdown, "net_amount");
        }

        return new CaptureResult(
            CaptureId: GetString(capture, "id") ?? throw Unexpected("Capture response had no id."),
            Status: GetString(capture, "status") ?? "UNKNOWN",
            GrossAmount: amount,
            PayPalFee: fee,
            NetAmount: net,
            CurrencyCode: currency ?? _options.CurrencyCode);
    }

    // ---------------------------------------------------------------- Reauthorize (Payments v2)

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new AmountOnlyModel { Amount = Money(amount, currencyCode) };
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            body, idempotencyKey, cancellationToken);
        var auth = await ReadJsonAsync(response, cancellationToken);

        var (authAmount, authCurrency) = ReadMoney(auth, "amount");
        return new AuthorizationResult(
            PayPalOrderId: string.Empty,
            AuthorizationId: GetString(auth, "id") ?? throw Unexpected("Reauthorize response had no id."),
            Status: GetString(auth, "status") ?? "UNKNOWN",
            Amount: authAmount,
            CurrencyCode: authCurrency ?? currencyCode,
            ExpiresAt: GetDateTime(auth, "expiration_time"));
    }

    // ---------------------------------------------------------------- Void (Payments v2)

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            content: null, idempotencyKey, cancellationToken);
        // 204 No Content on success; SendAsync already validated the status.
        _ = response;
    }

    // ---------------------------------------------------------------- Refund (Payments v2)

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object? body = amount.HasValue ? new AmountOnlyModel { Amount = Money(amount.Value, currencyCode) } : new { };
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            body, idempotencyKey, cancellationToken);
        var refund = await ReadJsonAsync(response, cancellationToken);

        var (refundAmount, refundCurrency) = ReadMoney(refund, "amount");
        return new RefundResult(
            RefundId: GetString(refund, "id") ?? throw Unexpected("Refund response had no id."),
            Status: GetString(refund, "status") ?? "UNKNOWN",
            Amount: amount ?? refundAmount,
            CurrencyCode: refundCurrency ?? currencyCode);
    }

    // ---------------------------------------------------------------- Vault (Vault v3)

    public async Task<VaultCardResult> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new VaultPaymentTokenRequestModel
        {
            PaymentSource = new PaymentSourceModel { Card = BuildCard(card, null) }
        };

        using var response = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, idempotencyKey, cancellationToken);
        var token = await ReadJsonAsync(response, cancellationToken);

        var vaultId = GetString(token, "id") ?? throw Unexpected("Vault response had no token id.");
        string brand = "CARD", lastDigits = "";
        string? expiry = null, name = null;
        if (token.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardResp))
        {
            brand = GetString(cardResp, "brand") ?? brand;
            lastDigits = GetString(cardResp, "last_digits") ?? lastDigits;
            expiry = GetString(cardResp, "expiry");
            name = GetString(cardResp, "name");
        }

        return new VaultCardResult(vaultId, brand, lastDigits, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}", content: null, idempotencyKey: null, cancellationToken);
        _ = response;
    }

    // ---------------------------------------------------------------- Reconciliation (Transaction Search v1)

    public async Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ReconciliationTransaction>();

        // Cover the whole range even when it exceeds PayPal's 31-day per-query limit.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxReportWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await ReadAllPagesAsync(windowStart, windowEnd, results, cancellationToken);
            windowStart = windowEnd;
        }

        return results;
    }

    private async Task ReadAllPagesAsync(DateTimeOffset from, DateTimeOffset to, List<ReconciliationTransaction> results,
        CancellationToken cancellationToken)
    {
        int page = 1;
        int totalPages;
        do
        {
            var query = "/v1/reporting/transactions" +
                        $"?start_date={Uri.EscapeDataString(FormatReportDate(from))}" +
                        $"&end_date={Uri.EscapeDataString(FormatReportDate(to))}" +
                        "&fields=transaction_info&balance_affecting_records_only=Y" +
                        $"&page_size=500&page={page}";

            using var response = await SendAsync(HttpMethod.Get, query, content: null, idempotencyKey: null, cancellationToken);
            var report = await ReadJsonAsync(response, cancellationToken);

            totalPages = report.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var t) ? t : 1;

            if (report.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (!detail.TryGetProperty("transaction_info", out var info))
                    {
                        continue;
                    }

                    var (amount, currency) = ReadMoney(info, "transaction_amount");
                    results.Add(new ReconciliationTransaction(
                        TransactionId: GetString(info, "transaction_id") ?? string.Empty,
                        InvoiceId: GetString(info, "invoice_id"),
                        Status: GetString(info, "transaction_status"),
                        Amount: amount,
                        CurrencyCode: currency ?? _options.CurrencyCode,
                        Date: GetDateTime(info, "transaction_initiation_date"),
                        FeeAmount: TryReadMoneyValue(info, "fee_amount"),
                        EventCode: GetString(info, "transaction_event_code")));
                }
            }

            page++;
        }
        while (page <= totalPages);
    }

    // ---------------------------------------------------------------- HTTP plumbing

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string pathAndQuery, object? content,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, BuildUri(pathAndQuery));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (content is not null)
        {
            var json = JsonSerializer.Serialize(content, content.GetType(), SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, method, pathAndQuery, cancellationToken);
        }

        return response;
    }

    private async Task ThrowApiExceptionAsync(HttpResponseMessage response, HttpMethod method, string path,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var debugId = response.Headers.TryGetValues("Paypal-Debug-Id", out var ids)
            ? string.Join(",", ids)
            : null;

        string? name = null;
        string? message = null;
        var detailIssues = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            name = GetString(root, "name") ?? GetString(root, "error");
            message = GetString(root, "message") ?? GetString(root, "error_description");
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in details.EnumerateArray())
                {
                    var issue = GetString(d, "issue");
                    if (!string.IsNullOrEmpty(issue))
                    {
                        detailIssues.Add(issue!);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall back to raw text below.
        }

        var issues = detailIssues.Count > 0 ? $" [{string.Join(", ", detailIssues)}]" : string.Empty;
        var summary = message ?? (string.IsNullOrWhiteSpace(payload) ? response.ReasonPhrase : payload);
        // Prefer a specific issue as the PayPal 'name' so callers (e.g. stale-auth handling) can react.
        var effectiveName = detailIssues.Count > 0 ? detailIssues[0] : name;

        _logger.LogWarning($"PayPal {method} {path} failed: {(int)response.StatusCode} {name}{issues} (debug-id {debugId}).");

        throw new PayPalApiException(
            $"PayPal {method} {path} returned {(int)response.StatusCode} {name}: {summary}{issues}",
            (int)response.StatusCode, debugId, effectiveName);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        // 60s safety margin so a token never expires mid-request.
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

            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            {
                throw new PaymentException("PayPal credentials are not configured (PayPal:ClientId / PayPal:ClientSecret).");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowApiExceptionAsync(response, HttpMethod.Post, "/v1/oauth2/token", cancellationToken);
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;
            _accessToken = GetString(root, "access_token") ?? throw Unexpected("Token response had no access_token.");
            var expiresIn = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var secs) ? secs : 300;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            _logger.LogInformation($"Acquired PayPal access token, valid for {expiresIn}s.");
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private Uri BuildUri(string pathAndQuery) => new(_options.ResolveBaseUrl() + pathAndQuery);

    // ---------------------------------------------------------------- helpers

    private MoneyModel Money(decimal amount, string currencyCode) => new()
    {
        CurrencyCode = currencyCode,
        Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static CardModel? BuildCard(CardDetails? card, string? vaultId)
    {
        if (vaultId is not null)
        {
            return new CardModel { VaultId = vaultId };
        }
        if (card is null)
        {
            return null;
        }

        CardBillingAddressModel? billing = null;
        if (card.BillingAddress is not null)
        {
            var b = card.BillingAddress;
            billing = new CardBillingAddressModel
            {
                AddressLine1 = b.AddressLine1,
                AddressLine2 = b.AddressLine2,
                AdminArea2 = b.AdminArea2,
                AdminArea1 = b.AdminArea1,
                PostalCode = b.PostalCode,
                CountryCode = b.CountryCode
            };
        }

        return new CardModel
        {
            Number = card.Number,
            Expiry = card.ExpiryYearMonth,
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = billing
        };
    }

    private static void GuardAgainstChallenge(JsonElement order)
    {
        var status = GetString(order, "status");
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (3-D Secure). " +
                "This integration does not perform a browser approval round-trip.");
        }

        if (order.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                var rel = GetString(link, "rel");
                if (string.Equals(rel, "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    throw new PaymentChallengeRequiredException(
                        "PayPal returned a payer-action link requiring browser approval (3-D Secure). " +
                        "This integration does not perform a browser approval round-trip.");
                }
            }
        }
    }

    private bool TryReadAuthorization(JsonElement order, out AuthorizationResult? result)
    {
        result = null;
        var orderId = GetString(order, "id") ?? string.Empty;

        if (!order.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var unit in units.EnumerateArray())
        {
            if (!unit.TryGetProperty("payments", out var payments) ||
                !payments.TryGetProperty("authorizations", out var auths) ||
                auths.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var auth in auths.EnumerateArray())
            {
                var authId = GetString(auth, "id");
                if (string.IsNullOrEmpty(authId))
                {
                    continue;
                }

                var (amount, currency) = ReadMoney(auth, "amount");
                result = new AuthorizationResult(
                    PayPalOrderId: orderId,
                    AuthorizationId: authId!,
                    Status: GetString(auth, "status") ?? "CREATED",
                    Amount: amount,
                    CurrencyCode: currency ?? _options.CurrencyCode,
                    ExpiresAt: GetDateTime(auth, "expiration_time"));
                return true;
            }
        }

        return false;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return default;
        }
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.Clone();
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? GetDateTime(JsonElement element, string property)
    {
        var raw = GetString(element, property);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
            ? dto
            : null;
    }

    private static (decimal Amount, string? Currency) ReadMoney(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var money) &&
            money.ValueKind == JsonValueKind.Object)
        {
            var value = GetString(money, "value");
            var currency = GetString(money, "currency_code");
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                return (amount, currency);
            }
            return (0m, currency);
        }
        return (0m, null);
    }

    private static decimal? TryReadMoneyValue(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var money) &&
            money.ValueKind == JsonValueKind.Object)
        {
            var value = GetString(money, "value");
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                return amount;
            }
        }
        return null;
    }

    private static string FormatReportDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static PaymentException Unexpected(string message) =>
        new($"Unexpected PayPal response: {message}");
}

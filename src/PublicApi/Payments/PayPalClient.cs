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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalClient : IPayPalClient
{
    private const int MaxAttempts = 3;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly string _baseUrl;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalSettings> settings,
        IMemoryCache cache, ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
        _baseUrl = ResolveBaseUrl(_settings);
    }

    public async Task<PayPalOrderResult> CreateOrderAsync(int orderId, string paymentReference, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = orderId.ToString(CultureInfo.InvariantCulture),
                    invoice_id = InvoiceId(paymentReference),
                    custom_id = paymentReference,
                    amount = Money(amount, currency)
                }
            }
        };
        using var json = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body,
            requestId, cancellationToken);
        return new PayPalOrderResult(RequiredString(json.RootElement, "id"),
            RequiredString(json.RootElement, "status"));
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string paypalOrderId,
        CardInput? card, string? vaultId, string requestId, CancellationToken cancellationToken)
    {
        object cardSource = card is not null
            ? Card(card)
            : new
            {
                vault_id = vaultId,
                stored_credential = new
                {
                    payment_initiator = "CUSTOMER",
                    payment_type = "ONE_TIME",
                    usage = "SUBSEQUENT"
                }
            };
        var body = new { payment_source = new { card = cardSource } };
        using var json = await SendAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(paypalOrderId)}/authorize", body,
            requestId, cancellationToken);
        ThrowIfPayerActionRequired(json.RootElement);

        var authorization = json.RootElement.GetProperty("purchase_units")[0]
            .GetProperty("payments").GetProperty("authorizations")[0];
        return ParseAuthorization(authorization);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null,
            null, cancellationToken);
        return ParseAuthorization(json.RootElement);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId,
        decimal amount, string currency, string requestId, CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = Money(amount, currency) }, requestId, cancellationToken);
        return ParseAuthorization(json.RootElement);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, int orderId, string paymentReference,
        decimal amount, string currency, string requestId, CancellationToken cancellationToken)
    {
        var body = new
        {
            amount = Money(amount, currency),
            invoice_id = InvoiceId(paymentReference),
            final_capture = true
        };
        using var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            body, requestId, cancellationToken);
        return ParseCapture(json.RootElement);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId,
        CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null,
            cancellationToken);
        return ParseCapture(json.RootElement);
    }

    public async Task VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            null, requestId, cancellationToken, expectJson: false);
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new { amount = Money(amount, currency) }, requestId, cancellationToken);
        var root = json.RootElement;
        var money = root.GetProperty("amount");
        return new PayPalRefundResult(RequiredString(root, "id"), RequiredString(root, "status"),
            Decimal(money, "value"), RequiredString(money, "currency_code"),
            Date(root, "create_time"));
    }

    public async Task<PayPalPaymentTokenResult> SaveCardAsync(string buyerId, CardInput card,
        string requestId, CancellationToken cancellationToken)
    {
        using var setup = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens",
            new { payment_source = new { card = Card(card) } }, $"{requestId}-setup",
            cancellationToken);
        ThrowIfPayerActionRequired(setup.RootElement);
        var status = RequiredString(setup.RootElement, "status");
        if (status is not ("APPROVED" or "TOKENIZED" or "VAULTED"))
            throw new PaymentConflictException($"PayPal returned setup-token status '{status}', so the card was not saved.");

        var setupTokenId = RequiredString(setup.RootElement, "id");
        var paymentTokenBody = new
        {
            payment_source = new { token = new { id = setupTokenId, type = "SETUP_TOKEN" } },
            customer = new { merchant_customer_id = buyerId }
        };
        using var token = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens",
            paymentTokenBody, $"{requestId}-token", cancellationToken);
        var tokenCard = token.RootElement.GetProperty("payment_source").GetProperty("card");
        return new PayPalPaymentTokenResult(RequiredString(token.RootElement, "id"),
            RequiredString(tokenCard, "brand"), RequiredString(tokenCard, "last_digits"),
            RequiredString(tokenCard, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(tokenId)}", null, null,
            cancellationToken, expectJson: false);
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<PayPalTransactionRecord>();
        var windowStart = from.ToUniversalTime();
        var finalEnd = to.ToUniversalTime();

        while (windowStart < finalEnd)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > finalEnd) windowEnd = finalEnd;

            const int pageSize = 500;
            for (var page = 1; ; page++)
            {
                var path = "/v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(Rfc3339(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(Rfc3339(windowEnd))}" +
                    $"&fields=transaction_info&page_size={pageSize}&page={page}";
                using var json = await SendAsync(HttpMethod.Get, path, null, null, cancellationToken);
                var details = json.RootElement.TryGetProperty("transaction_details", out var array)
                    ? array : default;
                var count = details.ValueKind == JsonValueKind.Array ? details.GetArrayLength() : 0;
                if (count == 0) break;

                foreach (var detail in details.EnumerateArray())
                    results.Add(ParseTransaction(detail.GetProperty("transaction_info")));
                if (count < pageSize) break;
            }

            windowStart = windowEnd;
        }

        return results
            .GroupBy(TransactionIdentity)
            .Select(group => group.First())
            .OrderBy(transaction => transaction.InitiatedAt)
            .ToList();
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken, bool expectJson = true)
    {
        var jsonBody = body is null ? null : JsonSerializer.Serialize(body, JsonOptions);
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var token = await GetAccessTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(method, ApiUri(path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (requestId is not null)
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            if (jsonBody is not null)
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return expectJson && !string.IsNullOrWhiteSpace(responseBody)
                    ? JsonDocument.Parse(responseBody)
                    : JsonDocument.Parse("{}");
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt < MaxAttempts)
            {
                _cache.Remove(TokenCacheKey());
                continue;
            }

            if (((int)response.StatusCode == 429 || (int)response.StatusCode >= 500) &&
                attempt < MaxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(3, attempt - 1)),
                    cancellationToken);
                continue;
            }

            throw CreateException(response.StatusCode, responseBody);
        }

        throw new InvalidOperationException("The PayPal request exhausted its retry policy.");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<string>(TokenCacheKey(), out var cached) && cached is not null)
            return cached;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue<string>(TokenCacheKey(), out cached) && cached is not null)
                return cached;

            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUri("/v1/oauth2/token"));
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8,
                "application/x-www-form-urlencoded");
            using var response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw CreateException(response.StatusCode, responseBody);

            using var json = JsonDocument.Parse(responseBody);
            var accessToken = RequiredString(json.RootElement, "access_token");
            var expiresIn = json.RootElement.GetProperty("expires_in").GetInt32();
            _cache.Set(TokenCacheKey(), accessToken,
                TimeSpan.FromSeconds(Math.Max(30, expiresIn - 60)));
            return accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private PayPalException CreateException(HttpStatusCode statusCode, string responseBody)
    {
        string? debugId = null;
        var issues = new List<string>();
        try
        {
            using var json = JsonDocument.Parse(responseBody);
            if (json.RootElement.TryGetProperty("debug_id", out var debug))
                debugId = debug.GetString();
            if (json.RootElement.TryGetProperty("details", out var details) &&
                details.ValueKind == JsonValueKind.Array)
            {
                issues.AddRange(details.EnumerateArray()
                    .Where(detail => detail.TryGetProperty("issue", out _))
                    .Select(detail => detail.GetProperty("issue").GetString())
                    .Where(issue => !string.IsNullOrWhiteSpace(issue))!);
            }
        }
        catch (JsonException) { }

        _logger.LogWarning("PayPal request failed with HTTP {StatusCode}; debug ID {DebugId}; issues {Issues}",
            (int)statusCode, debugId, string.Join(",", issues));
        var suffix = issues.Count == 0 ? string.Empty : $" ({string.Join(", ", issues)})";
        return new PayPalException(statusCode, $"PayPal rejected the request with HTTP {(int)statusCode}{suffix}.",
            debugId, issues);
    }

    private static PayPalAuthorizationResult ParseAuthorization(JsonElement root)
    {
        var money = root.GetProperty("amount");
        var createdAt = Date(root, "create_time");
        string? captureId = null;
        if (root.TryGetProperty("supplementary_data", out var supplementary) &&
            supplementary.TryGetProperty("related_ids", out var relatedIds))
            captureId = OptionalString(relatedIds, "capture_id");
        return new PayPalAuthorizationResult(RequiredString(root, "id"),
            RequiredString(root, "status"), Decimal(money, "value"),
            RequiredString(money, "currency_code"), createdAt,
            OptionalDate(root, "expiration_time") ?? createdAt.AddDays(29), captureId);
    }

    private static PayPalCaptureResult ParseCapture(JsonElement root)
    {
        var amount = root.GetProperty("amount");
        decimal fee = 0;
        decimal net = Decimal(amount, "value");
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            if (breakdown.TryGetProperty("paypal_fee", out var paypalFee))
                fee = Decimal(paypalFee, "value");
            if (breakdown.TryGetProperty("net_amount", out var netAmount))
                net = Decimal(netAmount, "value");
        }
        return new PayPalCaptureResult(RequiredString(root, "id"), RequiredString(root, "status"),
            Decimal(amount, "value"), RequiredString(amount, "currency_code"), fee, net,
            Date(root, "create_time"));
    }

    private static PayPalTransactionRecord ParseTransaction(JsonElement root)
    {
        var amount = root.GetProperty("transaction_amount");
        decimal? fee = null;
        if (root.TryGetProperty("fee_amount", out var feeAmount)) fee = Decimal(feeAmount, "value");
        return new PayPalTransactionRecord(RequiredString(root, "transaction_id"),
            OptionalString(root, "paypal_reference_id"), OptionalString(root, "paypal_reference_id_type"),
            OptionalString(root, "invoice_id"), RequiredString(root, "transaction_event_code"),
            RequiredString(root, "transaction_status"), Decimal(amount, "value"),
            RequiredString(amount, "currency_code"), fee, Date(root, "transaction_initiation_date"),
            Date(root, "transaction_updated_date"));
    }

    private static object Card(CardInput card) => new
    {
        name = card.Name,
        number = card.Number,
        expiry = card.Expiry,
        security_code = card.SecurityCode,
        billing_address = new
        {
            country_code = card.BillingAddress.CountryCode,
            address_line_1 = card.BillingAddress.AddressLine1,
            address_line_2 = card.BillingAddress.AddressLine2,
            admin_area_2 = card.BillingAddress.AdminArea2,
            admin_area_1 = card.BillingAddress.AdminArea1,
            postal_code = card.BillingAddress.PostalCode
        }
    };

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency,
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static void ThrowIfPayerActionRequired(JsonElement root)
    {
        if (OptionalString(root, "status") == "PAYER_ACTION_REQUIRED" ||
            (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array &&
             links.EnumerateArray().Any(link => OptionalString(link, "rel") == "payer-action")))
            throw new PayPalPayerActionRequiredException();
    }

    private Uri ApiUri(string path) => new($"{_baseUrl.TrimEnd('/')}/{path.TrimStart('/')}");
    private string TokenCacheKey() => $"PayPal.AccessToken:{_settings.ClientId}";
    private static string InvoiceId(string paymentReference) => $"eshop-{paymentReference}";
    private static string Rfc3339(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    private static string TransactionIdentity(PayPalTransactionRecord transaction) =>
        $"{transaction.TransactionId}|{transaction.EventCode}|{transaction.Amount}|{transaction.Currency}|{transaction.InitiatedAt:O}";

    private static string ResolveBaseUrl(PayPalSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl)) return settings.BaseUrl;
        return settings.Environment.Equals("Live", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }

    private static string RequiredString(JsonElement element, string property) =>
        element.GetProperty(property).GetString()
        ?? throw new JsonException($"PayPal response property '{property}' was null.");
    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() : null;
    private static decimal Decimal(JsonElement element, string property) =>
        decimal.Parse(RequiredString(element, property), NumberStyles.Number, CultureInfo.InvariantCulture);
    private static DateTimeOffset Date(JsonElement element, string property) =>
        DateTimeOffset.Parse(RequiredString(element, property), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal);
    private static DateTimeOffset? OptionalDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? DateTimeOffset.Parse(value.GetString()!, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal)
            : null;
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public interface IPayPalClient
{
    string Currency { get; }
    Task<PayPalAuthorizationResult> AuthorizeAsync(decimal amount, string referenceId, CardInput? card,
        string? paymentToken, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationState> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken);
    Task<PayPalAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string requestId,
        CancellationToken cancellationToken);
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string requestId,
        CancellationToken cancellationToken);
    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string requestId,
        CancellationToken cancellationToken);
    Task<PayPalVaultResult> SaveCardAsync(string merchantCustomerId, string? payPalCustomerId, CardInput card,
        string requestId, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record PayPalAuthorizationState(string Id, string Status, decimal Amount,
    DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);

public sealed record PayPalAuthorizationResult(string OrderId, string OrderStatus,
    PayPalAuthorizationState Authorization);

public sealed record PayPalCaptureResult(string Id, string Status, decimal Amount, decimal Fee,
    decimal Net, DateTimeOffset CreatedAt);

public sealed record PayPalRefundResult(string Id, string Status, decimal Amount);

public sealed record PayPalVaultResult(string PaymentTokenId, string CustomerId, string Brand,
    string Last4, string Expiry, string? CardholderName);

public sealed record PayPalTransaction(string TransactionId, string? ReferenceId, string? EventCode,
    string? Status, decimal? Amount, decimal? Fee, string? Currency, DateTimeOffset? InitiatedAt);

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string name, string message, string? issue,
        string? debugId) : base(message)
    {
        StatusCode = statusCode;
        Name = name;
        Issue = issue;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string Name { get; }
    public string? Issue { get; }
    public string? DebugId { get; }
    public bool RequiresPayerAction => Issue is "PAYER_ACTION_REQUIRED" or "CONTINGENCY";
}

public sealed class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly string _baseUrl;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _options.Validate();
        _baseUrl = _options.ResolveBaseUrl();
    }

    public string Currency => _options.Currency.Trim().ToUpperInvariant();

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(decimal amount, string referenceId,
        CardInput? card, string? paymentToken, string requestId, CancellationToken cancellationToken)
    {
        object paymentSource = card is not null
            ? new
            {
                card = new
                {
                    number = card.Number,
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    name = card.Name,
                    billing_address = new
                    {
                        address_line_1 = card.BillingAddress.AddressLine1,
                        address_line_2 = card.BillingAddress.AddressLine2,
                        admin_area_2 = card.BillingAddress.AdminArea2,
                        admin_area_1 = card.BillingAddress.AdminArea1,
                        postal_code = card.BillingAddress.PostalCode,
                        country_code = card.BillingAddress.CountryCode.ToUpperInvariant()
                    }
                }
            }
            : new { token = new { id = paymentToken, type = "PAYMENT_METHOD_TOKEN" } };

        var body = new
        {
            intent = "AUTHORIZE",
            payment_source = paymentSource,
            purchase_units = new[]
            {
                new
                {
                    reference_id = referenceId,
                    custom_id = referenceId,
                    amount = Money(amount)
                }
            }
        };

        using var document = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId,
            cancellationToken);
        var root = document.RootElement;
        var orderStatus = RequiredString(root, "status");
        if (orderStatus == "PAYER_ACTION_REQUIRED" || HasPayerActionLink(root))
        {
            throw new PayPalApiException(HttpStatusCode.UnprocessableEntity, "PAYER_ACTION_REQUIRED",
                "PayPal requires browser approval for this card payment.", "PAYER_ACTION_REQUIRED",
                OptionalString(root, "debug_id"));
        }

        var authorization = root.GetProperty("purchase_units")[0].GetProperty("payments")
            .GetProperty("authorizations")[0];
        var result = ParseAuthorization(authorization);
        EnsureAmount(amount, result.Amount, "authorization");
        return new PayPalAuthorizationResult(RequiredString(root, "id"), orderStatus, result);
    }

    public async Task<PayPalAuthorizationState> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null,
            cancellationToken);
        return ParseAuthorization(document.RootElement);
    }

    public async Task<PayPalAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount,
        string requestId, CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = Money(amount) }, requestId, cancellationToken);
        var result = ParseAuthorization(document.RootElement);
        EnsureAmount(amount, result.Amount, "reauthorization");
        return result;
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount,
        string requestId, CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new { amount = Money(amount), final_capture = true }, requestId, cancellationToken);
        return ParseCapture(document.RootElement, amount);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId,
        CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null, cancellationToken);
        return ParseCapture(document.RootElement, null);
    }

    private static PayPalCaptureResult ParseCapture(JsonElement root, decimal? expectedAmount)
    {
        var capturedAmount = MoneyValue(root.GetProperty("amount"));
        if (expectedAmount.HasValue) EnsureAmount(expectedAmount.Value, capturedAmount, "capture");
        var hasBreakdown = root.TryGetProperty("seller_receivable_breakdown", out var breakdown);
        var fee = hasBreakdown && breakdown.TryGetProperty("paypal_fee", out var feeElement)
            ? MoneyValue(feeElement)
            : 0m;
        var net = hasBreakdown && breakdown.TryGetProperty("net_amount", out var netElement)
            ? MoneyValue(netElement)
            : capturedAmount - fee;
        return new PayPalCaptureResult(RequiredString(root, "id"), RequiredString(root, "status"),
            capturedAmount, fee, net, OptionalDate(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void", new { }, requestId,
            cancellationToken, allowEmptyResponse: true);
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string requestId,
        CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new { amount = Money(amount) }, requestId, cancellationToken);
        var root = document.RootElement;
        var refundedAmount = MoneyValue(root.GetProperty("amount"));
        EnsureAmount(amount, refundedAmount, "refund");
        return new PayPalRefundResult(RequiredString(root, "id"), RequiredString(root, "status"),
            refundedAmount);
    }

    public async Task<PayPalVaultResult> SaveCardAsync(string merchantCustomerId, string? payPalCustomerId,
        CardInput card, string requestId, CancellationToken cancellationToken)
    {
        object customer = string.IsNullOrWhiteSpace(payPalCustomerId)
            ? new { merchant_customer_id = merchantCustomerId }
            : new { id = payPalCustomerId };
        var setupBody = new
        {
            customer,
            payment_source = new
            {
                card = new
                {
                    number = card.Number,
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    name = card.Name,
                    billing_address = new
                    {
                        address_line_1 = card.BillingAddress.AddressLine1,
                        address_line_2 = card.BillingAddress.AddressLine2,
                        admin_area_2 = card.BillingAddress.AdminArea2,
                        admin_area_1 = card.BillingAddress.AdminArea1,
                        postal_code = card.BillingAddress.PostalCode,
                        country_code = card.BillingAddress.CountryCode.ToUpperInvariant()
                    }
                }
            }
        };

        using var setupDocument = await SendJsonAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody,
            requestId + "-setup", cancellationToken);
        var setup = setupDocument.RootElement;
        var setupStatus = RequiredString(setup, "status");
        if (setupStatus == "PAYER_ACTION_REQUIRED" || HasPayerActionLink(setup))
        {
            throw new PayPalApiException(HttpStatusCode.UnprocessableEntity, "PAYER_ACTION_REQUIRED",
                "PayPal requires browser approval before this card can be saved.", "PAYER_ACTION_REQUIRED",
                OptionalString(setup, "debug_id"));
        }

        var setupTokenId = RequiredString(setup, "id");
        using var tokenDocument = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens",
            new { payment_source = new { token = new { id = setupTokenId, type = "SETUP_TOKEN" } } },
            requestId + "-token", cancellationToken);
        var token = tokenDocument.RootElement;
        var tokenCard = token.GetProperty("payment_source").GetProperty("card");
        var customerId = RequiredString(token.GetProperty("customer"), "id");
        return new PayPalVaultResult(RequiredString(token, "id"), customerId,
            RequiredString(tokenCard, "brand"), RequiredString(tokenCard, "last_digits"),
            RequiredString(tokenCard, "expiry"), OptionalString(tokenCard, "name"));
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await SendJsonAsync(HttpMethod.Delete,
                $"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}", null, null,
                cancellationToken, allowEmptyResponse: true);
        }
        catch (PayPalApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // The desired state already exists in PayPal's vault.
        }
    }

    public async Task<IReadOnlyCollection<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<PayPalTransaction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var windowStart = from;

        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31) < to ? windowStart.AddDays(31) : to;
            var page = 1;
            while (true)
            {
                var query = $"start_date={EscapeDate(windowStart)}&end_date={EscapeDate(windowEnd)}" +
                    $"&fields=transaction_info&balance_affecting_records_only=N&page_size=500&page={page}";
                using var document = await SendJsonAsync(HttpMethod.Get,
                    "/v1/reporting/transactions?" + query, null, null, cancellationToken);
                var root = document.RootElement;
                if (root.TryGetProperty("transaction_details", out var details))
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        var info = detail.GetProperty("transaction_info");
                        var transaction = ParseTransaction(info);
                        var key = string.Join('|', transaction.TransactionId, transaction.EventCode,
                            transaction.InitiatedAt?.ToString("O", CultureInfo.InvariantCulture));
                        if (seen.Add(key)) results.Add(transaction);
                    }
                }

                var totalPages = OptionalInt(root, "total_pages") ?? page;
                if (page >= totalPages) break;
                page++;
            }

            windowStart = windowEnd;
        }

        return results;
    }

    public static string StableRequestId(string scope, string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"eshop-{scope}-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private object Money(decimal amount) => new
    {
        currency_code = Currency,
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken, bool allowEmptyResponse = false)
    {
        var json = body is null ? null : JsonSerializer.Serialize(body, JsonOptions);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var request = new HttpRequestMessage(method, _baseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                await GetAccessTokenAsync(cancellationToken));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (!string.IsNullOrWhiteSpace(requestId))
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    if (string.IsNullOrWhiteSpace(responseText))
                    {
                        if (allowEmptyResponse) return JsonDocument.Parse("{}");
                        throw new PayPalApiException(response.StatusCode, "EMPTY_RESPONSE",
                            "PayPal returned an empty response.", null, null);
                    }

                    return JsonDocument.Parse(responseText);
                }

                if (IsTransient(response.StatusCode) && attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                    continue;
                }

                throw ParseApiException(response.StatusCode, responseText);
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
            }
        }

        throw new HttpRequestException("PayPal was unavailable after three attempts.");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return _accessToken;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
                return _accessToken;

            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw ParseApiException(response.StatusCode, responseText);
            using var document = JsonDocument.Parse(responseText);
            _accessToken = RequiredString(document.RootElement, "access_token");
            var expiresIn = OptionalInt(document.RootElement, "expires_in") ?? 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static PayPalAuthorizationState ParseAuthorization(JsonElement element) => new(
        RequiredString(element, "id"), RequiredString(element, "status"),
        MoneyValue(element.GetProperty("amount")), OptionalDate(element, "create_time") ?? DateTimeOffset.UtcNow,
        OptionalDate(element, "expiration_time"));

    private static PayPalTransaction ParseTransaction(JsonElement info)
    {
        decimal? amount = null;
        decimal? fee = null;
        string? currency = null;
        if (info.TryGetProperty("transaction_amount", out var amountElement))
        {
            amount = MoneyValue(amountElement);
            currency = OptionalString(amountElement, "currency_code");
        }
        if (info.TryGetProperty("fee_amount", out var feeElement)) fee = MoneyValue(feeElement);

        return new PayPalTransaction(RequiredString(info, "transaction_id"),
            OptionalString(info, "paypal_reference_id"), OptionalString(info, "transaction_event_code"),
            OptionalString(info, "transaction_status"), amount, fee, currency,
            OptionalDate(info, "transaction_initiation_date"));
    }

    private static PayPalApiException ParseApiException(HttpStatusCode statusCode, string responseText)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            var issue = root.TryGetProperty("details", out var details) && details.GetArrayLength() > 0
                ? OptionalString(details[0], "issue")
                : null;
            return new PayPalApiException(statusCode, OptionalString(root, "name") ?? "PAYPAL_ERROR",
                OptionalString(root, "message") ?? "PayPal rejected the request.", issue,
                OptionalString(root, "debug_id"));
        }
        catch (JsonException)
        {
            return new PayPalApiException(statusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null, null);
        }
    }

    private static bool HasPayerActionLink(JsonElement root) =>
        root.TryGetProperty("links", out var links) && links.EnumerateArray().Any(link =>
            OptionalString(link, "rel") is "payer-action" or "approve");

    private static string RequiredString(JsonElement element, string name) =>
        element.GetProperty(name).GetString() ?? throw new JsonException($"PayPal field '{name}' was null.");

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? OptionalInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static DateTimeOffset? OptionalDate(JsonElement element, string name) =>
        DateTimeOffset.TryParse(OptionalString(element, name), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var value) ? value : null;

    private static decimal MoneyValue(JsonElement money) =>
        decimal.Parse(RequiredString(money, "value"), NumberStyles.Number, CultureInfo.InvariantCulture);

    private static void EnsureAmount(decimal expected, decimal actual, string operation)
    {
        if (decimal.Round(expected, 2) != decimal.Round(actual, 2))
            throw new PayPalApiException(HttpStatusCode.BadGateway, "AMOUNT_MISMATCH",
                $"PayPal's {operation} amount did not match the eShop order total.", "AMOUNT_MISMATCH", null);
    }

    private static bool IsTransient(HttpStatusCode statusCode) => statusCode is HttpStatusCode.RequestTimeout
        or HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
        or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private static string EscapeDate(DateTimeOffset value) => Uri.EscapeDataString(
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
}

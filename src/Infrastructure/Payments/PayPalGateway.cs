using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalGateway(HttpClient httpClient, IOptions<PayPalOptions> options, ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string Currency => _options.Currency.ToUpperInvariant();

    public async Task<PayPalOrderResult> CreateOrderAsync(int orderId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = "default",
                    custom_id = orderId.ToString(CultureInfo.InvariantCulture),
                    description = $"eShopOnWeb order {orderId}",
                    amount = Money(amount, currency)
                }
            }
        };
        using var json = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId,
            cancellationToken);
        EnsureNoShopperAction(json.RootElement, "PayPal required browser approval to create this card payment.");
        return new PayPalOrderResult(RequiredString(json.RootElement, "id"),
            RequiredString(json.RootElement, "status"));
    }

    public async Task<AuthorizationResult> AuthorizeAsync(string payPalOrderId, PaymentSource source,
        string requestId, CancellationToken cancellationToken)
    {
        object card = source.Card is not null
            ? CardPayload(source.Card)
            : new
            {
                vault_id = source.VaultId,
                stored_credential = new
                {
                    payment_initiator = "CUSTOMER",
                    payment_type = "ONE_TIME",
                    usage = "SUBSEQUENT"
                }
            };
        var body = new { payment_source = new { card } };
        using var json = await SendAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize", body, requestId,
            cancellationToken);
        EnsureNoShopperAction(json.RootElement,
            "PayPal required a browser card challenge; this headless integration cannot continue.");
        var authorization = json.RootElement.GetProperty("purchase_units")[0]
            .GetProperty("payments").GetProperty("authorizations")[0];
        return ReadAuthorization(authorization);
    }

    public async Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null,
            cancellationToken);
        return ReadAuthorization(json.RootElement);
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = Money(amount, currency) }, requestId, cancellationToken);
        return ReadAuthorization(json.RootElement);
    }

    public async Task<AuthorizationResult> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            new { }, requestId, cancellationToken);
        return ReadAuthorization(json.RootElement);
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new { amount = Money(amount, currency), final_capture = true }, requestId, cancellationToken);
        return ReadCapture(json.RootElement);
    }

    public async Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null, cancellationToken);
        return ReadCapture(json.RootElement);
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new { amount = Money(amount, currency) }, requestId, cancellationToken);
        var root = json.RootElement;
        var money = root.GetProperty("amount");
        return new RefundResult(RequiredString(root, "id"), RequiredString(root, "status"),
            ReadDecimal(money, "value"), RequiredString(money, "currency_code"));
    }

    public async Task<VaultedCardResult> SaveCardAsync(string merchantCustomerId, CardDetails card,
        string requestId, CancellationToken cancellationToken)
    {
        using var setup = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens",
            new
            {
                payment_source = new
                {
                    card = new
                    {
                        card.Name,
                        card.Number,
                        card.Expiry,
                        card.SecurityCode,
                        billing_address = BillingAddressPayload(card.BillingAddress)
                    }
                },
                customer = new { merchant_customer_id = merchantCustomerId }
            }, requestId + "-setup", cancellationToken);
        EnsureNoShopperAction(setup.RootElement,
            "PayPal required a browser card challenge while saving this card; this headless integration cannot continue.");
        var setupId = RequiredString(setup.RootElement, "id");
        var payPalCustomerId = TryGetString(setup.RootElement, "customer", "id");
        object customer = payPalCustomerId is not null
            ? new { id = payPalCustomerId }
            : new { merchant_customer_id = merchantCustomerId };

        using var token = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens",
            new
            {
                payment_source = new { token = new { id = setupId, type = "SETUP_TOKEN" } },
                customer
            }, requestId + "-token", cancellationToken);
        EnsureNoShopperAction(token.RootElement,
            "PayPal required browser approval while saving this card; this headless integration cannot continue.");
        var root = token.RootElement;
        var savedCard = root.GetProperty("payment_source").GetProperty("card");
        var customerId = TryGetString(root, "customer", "id");
        return new VaultedCardResult(RequiredString(root, "id"), customerId,
            RequiredString(savedCard, "brand"), RequiredString(savedCard, "last_digits"),
            RequiredString(savedCard, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await SendAsync(HttpMethod.Delete,
                $"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}", null, null,
                cancellationToken, allowEmptyBody: true);
        }
        catch (PaymentOperationException ex) when (ex.Kind == PaymentErrorKind.NotFound)
        {
            // A retry after PayPal deleted the token but before our database commit is successful in effect.
        }
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var page = 1;
        var results = new List<PayPalTransaction>();
        while (true)
        {
            var query = $"?start_date={Uri.EscapeDataString(FormatDate(from))}" +
                        $"&end_date={Uri.EscapeDataString(FormatDate(to))}" +
                        $"&fields=transaction_info&balance_affecting_records_only=N&page_size={pageSize}&page={page}";
            using var json = await SendAsync(HttpMethod.Get, "/v1/reporting/transactions" + query,
                null, null, cancellationToken);
            var root = json.RootElement;
            var count = 0;
            if (root.TryGetProperty("transaction_details", out var details))
            {
                foreach (var detail in details.EnumerateArray())
                {
                    count++;
                    var info = detail.GetProperty("transaction_info");
                    results.Add(new PayPalTransaction(
                        RequiredString(info, "transaction_id"), TryGetString(info, "paypal_reference_id"),
                        TryGetString(info, "invoice_id"), TryGetString(info, "custom_field"),
                        TryGetDate(info, "transaction_initiation_date"),
                        TryGetDate(info, "transaction_updated_date"), TryGetMoney(info, "transaction_amount"),
                        TryGetMoneyCurrency(info, "transaction_amount"), TryGetMoney(info, "fee_amount"),
                        TryGetString(info, "transaction_status"), TryGetString(info, "transaction_event_code")));
                }
            }
            var totalPages = TryGetInt(root, "total_pages");
            if ((totalPages.HasValue && page >= totalPages.Value) ||
                (!totalPages.HasValue && count < pageSize)) break;
            page++;
        }
        return results;
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken, bool allowEmptyBody = false)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var token = await GetAccessTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(method, BuildUri(path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (requestId is not null) request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _accessToken = null;
                continue;
            }
            if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * (1 << attempt) + Random.Shared.Next(25, 150)),
                    cancellationToken);
                continue;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) ThrowPayPalError(response.StatusCode, content, method, path);
            if (string.IsNullOrWhiteSpace(content))
            {
                if (!allowEmptyBody) throw new PaymentOperationException(PaymentErrorKind.PayPalUnavailable,
                    "PayPal returned an empty response where payment details were required.");
                return JsonDocument.Parse("{}");
            }
            return JsonDocument.Parse(content);
        }
        throw new PaymentOperationException(PaymentErrorKind.PayPalUnavailable,
            "PayPal did not complete the request after safe retries.");
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
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) ThrowPayPalError(response.StatusCode, content, HttpMethod.Post,
                "/v1/oauth2/token");
            using var json = JsonDocument.Parse(content);
            _accessToken = RequiredString(json.RootElement, "access_token");
            var expiresIn = TryGetInt(json.RootElement, "expires_in") ?? 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private Uri BuildUri(string path)
    {
        var configured = _options.BaseUrl;
        var baseUrl = string.IsNullOrWhiteSpace(configured)
            ? (_options.Environment.Equals("sandbox", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.sandbox.paypal.com"
                : "https://api-m.paypal.com")
            : configured.TrimEnd('/');
        return new Uri(baseUrl + path, UriKind.Absolute);
    }

    private void ThrowPayPalError(HttpStatusCode status, string content, HttpMethod method, string path)
    {
        string? debugId = null;
        string? name = null;
        var issues = new List<string>();
        try
        {
            using var json = JsonDocument.Parse(content);
            debugId = TryGetString(json.RootElement, "debug_id");
            name = TryGetString(json.RootElement, "name");
            if (json.RootElement.TryGetProperty("details", out var details))
                issues.AddRange(details.EnumerateArray().Select(x =>
                {
                    var issue = TryGetString(x, "issue");
                    var field = TryGetString(x, "field");
                    return issue is null ? null : field is null ? issue : $"{issue}@{field}";
                }).OfType<string>());
        }
        catch (JsonException) { }
        _logger.LogWarning("PayPal {Method} {Path} failed with {Status}; debug_id={DebugId}; issues={Issues}",
            method, path, (int)status, debugId, string.Join(',', issues));
        var kind = status switch
        {
            HttpStatusCode.BadRequest => PaymentErrorKind.InvalidRequest,
            HttpStatusCode.NotFound => PaymentErrorKind.NotFound,
            HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity => PaymentErrorKind.Conflict,
            _ => PaymentErrorKind.PayPalUnavailable
        };
        var issueText = issues.Count > 0 ? $" ({string.Join(", ", issues)})" : string.Empty;
        throw new PaymentOperationException(kind,
            $"PayPal could not complete the payment operation: {name ?? status.ToString()}{issueText}." +
            (debugId is null ? string.Empty : $" PayPal debug ID: {debugId}."), debugId);
    }

    private static object CardPayload(CardDetails card) => new
    {
        card.Name,
        card.Number,
        card.Expiry,
        card.SecurityCode,
        billing_address = BillingAddressPayload(card.BillingAddress)
    };

    private static object BillingAddressPayload(CardBillingAddress address) => new
    {
        country_code = address.CountryCode,
        address_line_1 = address.AddressLine1,
        address_line_2 = address.AddressLine2,
        admin_area_2 = address.City,
        admin_area_1 = address.State,
        postal_code = address.PostalCode
    };

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency.ToUpperInvariant(),
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static AuthorizationResult ReadAuthorization(JsonElement root)
    {
        var amount = root.GetProperty("amount");
        return new AuthorizationResult(RequiredString(root, "id"), RequiredString(root, "status"),
            ReadDecimal(amount, "value"), RequiredString(amount, "currency_code"),
            TryGetDate(root, "create_time") ?? DateTimeOffset.UtcNow, TryGetDate(root, "expiration_time"),
            TryGetString(root, "supplementary_data", "related_ids", "capture_id"));
    }

    private static CaptureResult ReadCapture(JsonElement root)
    {
        var amount = root.GetProperty("amount");
        return new CaptureResult(RequiredString(root, "id"), RequiredString(root, "status"),
            ReadDecimal(amount, "value"), RequiredString(amount, "currency_code"),
            TryGetMoney(root, "seller_receivable_breakdown", "paypal_fee"),
            TryGetMoney(root, "seller_receivable_breakdown", "net_amount"), TryGetDate(root, "create_time"));
    }

    private static void EnsureNoShopperAction(JsonElement root, string message)
    {
        if (TryGetString(root, "status") == "PAYER_ACTION_REQUIRED" ||
            (root.TryGetProperty("links", out var links) && links.EnumerateArray().Any(link =>
                string.Equals(TryGetString(link, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase))))
            throw new PaymentOperationException(PaymentErrorKind.ShopperActionRequired, message);
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    private static string RequiredString(JsonElement element, string property) =>
        element.GetProperty(property).GetString() ?? throw new JsonException($"Missing {property}.");
    private static string? TryGetString(JsonElement element, params string[] path)
    {
        foreach (var property in path)
            if (!element.TryGetProperty(property, out element)) return null;
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }
    private static DateTimeOffset? TryGetDate(JsonElement element, params string[] path) =>
        DateTimeOffset.TryParse(TryGetString(element, path), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var value) ? value : null;
    private static int? TryGetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : null;
    private static decimal ReadDecimal(JsonElement element, string property) =>
        decimal.Parse(RequiredString(element, property), NumberStyles.Number, CultureInfo.InvariantCulture);
    private static decimal? TryGetMoney(JsonElement element, params string[] path)
    {
        foreach (var property in path)
            if (!element.TryGetProperty(property, out element)) return null;
        return element.TryGetProperty("value", out var value) &&
               decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number : null;
    }
    private static string? TryGetMoneyCurrency(JsonElement element, string property) =>
        element.TryGetProperty(property, out var money) ? TryGetString(money, "currency_code") : null;
}

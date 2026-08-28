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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly IMemoryCache _cache;
    private readonly string _tokenCacheKey;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options, IMemoryCache cache)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _options.Validate();
        _httpClient.BaseAddress = _options.ResolveBaseUri();
        _cache = cache;
        _tokenCacheKey = $"paypal-access-token:{_httpClient.BaseAddress}:{_options.ClientId}";
    }

    public async Task<PayPalOrderResult> CreateOrderAsync(int orderId, string paymentReference, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = orderId.ToString(CultureInfo.InvariantCulture),
                    custom_id = paymentReference,
                    invoice_id = InvoiceId(paymentReference),
                    amount = Money(amount, currency)
                }
            }
        };

        using var json = await SendAsync(() => JsonRequest(HttpMethod.Post, "v2/checkout/orders", body, requestId),
            cancellationToken);
        return new PayPalOrderResult(RequiredString(json.RootElement, "id"),
            RequiredString(json.RootElement, "status"));
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string payPalOrderId,
        CardDetails? card, string? vaultId, string requestId, CancellationToken cancellationToken)
    {
        object paymentCard = card is not null
            ? CardRequest(card)
            : new Dictionary<string, object?> { ["vault_id"] = vaultId };
        var body = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = paymentCard }
        };

        using var json = await SendAsync(
            () => JsonRequest(HttpMethod.Post,
                $"v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize", body, requestId),
            cancellationToken);
        var root = json.RootElement;
        ThrowIfPayerActionRequired(root);
        var authorization = root.GetProperty("purchase_units")[0]
            .GetProperty("payments").GetProperty("authorizations")[0];
        var money = authorization.GetProperty("amount");
        return new PayPalAuthorizationResult(
            RequiredString(root, "id"), RequiredString(root, "status"),
            RequiredString(authorization, "id"), RequiredString(authorization, "status"),
            RequiredDecimal(money, "value"), RequiredString(money, "currency_code"),
            OptionalDate(authorization, "create_time"), OptionalDate(authorization, "expiration_time"));
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        using var json = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get,
                $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}"), cancellationToken);
        return ParseAuthorization(json.RootElement);
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(string authorizationId,
        decimal amount, string currency, string requestId, CancellationToken cancellationToken)
    {
        var body = new { amount = Money(amount, currency) };
        using var json = await SendAsync(
            () => JsonRequest(HttpMethod.Post,
                $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
                body, requestId), cancellationToken);
        return ParseAuthorization(json.RootElement);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount,
        string currency, string invoiceId, string requestId, CancellationToken cancellationToken)
    {
        var body = new
        {
            amount = Money(amount, currency),
            invoice_id = invoiceId,
            final_capture = true
        };
        using var json = await SendAsync(
            () => JsonRequest(HttpMethod.Post,
                $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
                body, requestId), cancellationToken);
        return ParseCapture(json.RootElement);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId,
        CancellationToken cancellationToken)
    {
        using var json = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get,
                $"v2/payments/captures/{Uri.EscapeDataString(captureId)}"), cancellationToken);
        return ParseCapture(json.RootElement);
    }

    private static PayPalCaptureResult ParseCapture(JsonElement root)
    {
        var captureMoney = root.GetProperty("amount");
        decimal? fee = null;
        decimal? net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = OptionalMoneyValue(breakdown, "paypal_fee");
            net = OptionalMoneyValue(breakdown, "net_amount");
        }
        return new PayPalCaptureResult(RequiredString(root, "id"), RequiredString(root, "status"),
            RequiredDecimal(captureMoney, "value"), RequiredString(captureMoney, "currency_code"),
            fee, net, OptionalDate(root, "create_time"));
    }

    public async Task VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(
            () => Request(HttpMethod.Post,
                $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void", requestId),
            cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount,
        string currency, string customId, string requestId, CancellationToken cancellationToken)
    {
        var body = new { amount = Money(amount, currency), custom_id = customId };
        using var json = await SendAsync(
            () => JsonRequest(HttpMethod.Post,
                $"v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund", body, requestId),
            cancellationToken);
        var root = json.RootElement;
        var refundMoney = root.GetProperty("amount");
        decimal? fee = null;
        decimal? net = null;
        if (root.TryGetProperty("seller_payable_breakdown", out var breakdown))
        {
            fee = OptionalMoneyValue(breakdown, "paypal_fee");
            net = OptionalMoneyValue(breakdown, "net_amount");
        }
        return new PayPalRefundResult(RequiredString(root, "id"), RequiredString(root, "status"),
            RequiredDecimal(refundMoney, "value"), RequiredString(refundMoney, "currency_code"),
            fee, net, OptionalDate(root, "update_time"));
    }

    public async Task<PayPalPaymentTokenResult> CreatePaymentTokenAsync(string merchantCustomerId,
        CardDetails card, string requestId, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["customer"] = new Dictionary<string, object?>
            {
                ["merchant_customer_id"] = merchantCustomerId
            },
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = CardRequest(card)
            }
        };
        using var json = await SendAsync(
            () => JsonRequest(HttpMethod.Post, "v3/vault/payment-tokens", body, requestId),
            cancellationToken);
        var root = json.RootElement;
        ThrowIfPayerActionRequired(root);
        var cardResponse = root.GetProperty("payment_source").GetProperty("card");
        var customerId = root.TryGetProperty("customer", out var customer)
            ? OptionalString(customer, "id") ?? merchantCustomerId
            : merchantCustomerId;
        return new PayPalPaymentTokenResult(RequiredString(root, "id"), customerId,
            RequiredString(cardResponse, "brand"), RequiredString(cardResponse, "last_digits"),
            RequiredString(cardResponse, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(
            () => new HttpRequestMessage(HttpMethod.Delete,
                $"v3/vault/payment-tokens/{Uri.EscapeDataString(tokenId)}"), cancellationToken);
    }

    public async Task<PayPalTransactionPage> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = string.Join("&", new Dictionary<string, string>
        {
            ["start_date"] = FormatDate(from),
            ["end_date"] = FormatDate(to),
            ["fields"] = "transaction_info",
            ["balance_affecting_records_only"] = "Y",
            ["page_size"] = pageSize.ToString(CultureInfo.InvariantCulture),
            ["page"] = page.ToString(CultureInfo.InvariantCulture)
        }.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}"));
        using var json = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"v1/reporting/transactions?{query}"),
            cancellationToken);
        var root = json.RootElement;
        var transactions = new List<PayPalTransaction>();
        if (root.TryGetProperty("transaction_details", out var details))
        {
            foreach (var detail in details.EnumerateArray())
            {
                var info = detail.GetProperty("transaction_info");
                transactions.Add(new PayPalTransaction(
                    RequiredString(info, "transaction_id"), OptionalString(info, "paypal_reference_id"),
                    OptionalString(info, "paypal_reference_id_type"), OptionalString(info, "invoice_id"),
                    OptionalString(info, "custom_field"), OptionalString(info, "transaction_event_code"),
                    OptionalString(info, "transaction_status"), OptionalMoneyValue(info, "transaction_amount"),
                    OptionalMoneyValue(info, "fee_amount"), OptionalMoneyCurrency(info, "transaction_amount"),
                    OptionalDate(info, "transaction_initiation_date"),
                    OptionalDate(info, "transaction_updated_date")));
            }
        }
        return new PayPalTransactionPage(transactions,
            OptionalInt(root, "page") ?? page, OptionalInt(root, "total_pages") ?? page);
    }

    private async Task<JsonDocument> SendAsync(Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(requestFactory, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task<HttpResponseMessage> SendRawAsync(Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = requestFactory();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                await GetAccessTokenAsync(attempt > 0, cancellationToken));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                response.Dispose();
                _cache.Remove(_tokenCacheKey);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                await ThrowPayPalErrorAsync(response, cancellationToken);
            }
            return response;
        }
        throw new InvalidOperationException("PayPal authentication failed after token refresh.");
    }

    private async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _cache.TryGetValue<string>(_tokenCacheKey, out var token)) return token!;
        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _cache.TryGetValue<string>(_tokenCacheKey, out token)) return token!;
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) await ThrowPayPalErrorAsync(response, cancellationToken);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            token = RequiredString(json.RootElement, "access_token");
            var expiresIn = OptionalInt(json.RootElement, "expires_in") ?? 300;
            _cache.Set(_tokenCacheKey, token, TimeSpan.FromSeconds(Math.Max(30, expiresIn - 60)));
            return token;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private static async Task ThrowPayPalErrorAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string name = "PAYPAL_ERROR";
        string message = $"PayPal returned HTTP {(int)response.StatusCode}.";
        string? debugId = null;
        string? issue = null;
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var root = json.RootElement;
            name = OptionalString(root, "name") ?? OptionalString(root, "error") ?? name;
            message = OptionalString(root, "message") ?? OptionalString(root, "error_description") ?? message;
            debugId = OptionalString(root, "debug_id");
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array &&
                details.GetArrayLength() > 0)
            {
                issue = OptionalString(details[0], "issue");
                message = OptionalString(details[0], "description") ?? message;
            }
        }
        catch (JsonException) { }

        if (name.Contains("PAYER_ACTION", StringComparison.OrdinalIgnoreCase) ||
            (issue?.Contains("PAYER_ACTION", StringComparison.OrdinalIgnoreCase) ?? false))
            throw new PayPalPayerActionRequiredException();
        throw new PayPalApiException(response.StatusCode, name, message, debugId, issue);
    }

    private static HttpRequestMessage JsonRequest(HttpMethod method, string path, object body,
        string? requestId = null)
    {
        var request = Request(method, path, requestId);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        return request;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? requestId = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (requestId is not null) request.Headers.Add("PayPal-Request-Id", requestId);
        request.Headers.Add("Prefer", "return=representation");
        return request;
    }

    private static Dictionary<string, object?> CardRequest(CardDetails card) => new()
    {
        ["name"] = card.Name,
        ["number"] = DigitsOnly(card.Number),
        ["expiry"] = card.Expiry,
        ["security_code"] = card.SecurityCode,
        ["billing_address"] = new Dictionary<string, object?>
        {
            ["address_line_1"] = card.BillingAddress.AddressLine1,
            ["address_line_2"] = card.BillingAddress.AddressLine2,
            ["admin_area_2"] = card.BillingAddress.City,
            ["admin_area_1"] = card.BillingAddress.State,
            ["postal_code"] = card.BillingAddress.PostalCode,
            ["country_code"] = card.BillingAddress.CountryCode.ToUpperInvariant()
        }
    };

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency.ToUpperInvariant(),
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    public static string InvoiceId(string paymentReference) => $"ESHOP-{paymentReference}";
    private static string DigitsOnly(string value) => new(value.Where(char.IsDigit).ToArray());
    private static string FormatDate(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static PayPalAuthorizationDetails ParseAuthorization(JsonElement root)
    {
        var money = root.GetProperty("amount");
        return new PayPalAuthorizationDetails(RequiredString(root, "id"), RequiredString(root, "status"),
            RequiredDecimal(money, "value"), RequiredString(money, "currency_code"),
            OptionalDate(root, "create_time"), OptionalDate(root, "expiration_time"));
    }

    private static void ThrowIfPayerActionRequired(JsonElement root)
    {
        if (OptionalString(root, "status") == "PAYER_ACTION_REQUIRED")
            throw new PayPalPayerActionRequiredException();
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array &&
            links.EnumerateArray().Any(x => OptionalString(x, "rel") == "payer-action"))
            throw new PayPalPayerActionRequiredException();
    }

    private static string RequiredString(JsonElement element, string property) =>
        element.GetProperty(property).GetString() ?? throw new JsonException($"PayPal response omitted {property}.");
    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static decimal RequiredDecimal(JsonElement element, string property) =>
        decimal.Parse(RequiredString(element, property), NumberStyles.Number, CultureInfo.InvariantCulture);
    private static int? OptionalInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : null;
    private static DateTimeOffset? OptionalDate(JsonElement element, string property) =>
        DateTimeOffset.TryParse(OptionalString(element, property), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var date) ? date : null;
    private static decimal? OptionalMoneyValue(JsonElement element, string property) =>
        element.TryGetProperty(property, out var money) && money.ValueKind == JsonValueKind.Object &&
        decimal.TryParse(OptionalString(money, "value"), NumberStyles.Number, CultureInfo.InvariantCulture,
            out var amount) ? amount : null;
    private static string? OptionalMoneyCurrency(JsonElement element, string property) =>
        element.TryGetProperty(property, out var money) && money.ValueKind == JsonValueKind.Object
            ? OptionalString(money, "currency_code") : null;
}

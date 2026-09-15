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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalClient : IPayPalClient
{
    private const int TransactionPageSize = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
        _options = options.Value;
    }

    public async Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency, string paymentReference,
        string requestId, CancellationToken cancellationToken)
    {
        var reference = paymentReference;
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = reference,
                    custom_id = reference,
                    invoice_id = reference,
                    amount = Money(amount, currency)
                }
            }
        };
        var root = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId, cancellationToken);
        return new PayPalOrderResult(RequiredString(root, "id"), RequiredString(root, "status"));
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string payPalOrderId, PayPalCard? card,
        string? vaultId, string requestId, CancellationToken cancellationToken)
    {
        object cardSource = card is not null
            ? CardBody(card)
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
        var root = await SendJsonAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize", body, requestId, cancellationToken);
        ThrowIfChallenge(root);

        var authorization = root.GetProperty("purchase_units")[0]
            .GetProperty("payments").GetProperty("authorizations")[0];
        return ParseAuthorization(authorization);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        var root = await SendJsonAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null, cancellationToken);
        return ParseAuthorization(root);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        var root = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = Money(amount, currency) }, requestId, cancellationToken);
        return ParseAuthorization(root);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string paymentReference, string requestId, CancellationToken cancellationToken)
    {
        var root = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new
            {
                amount = Money(amount, currency),
                invoice_id = paymentReference,
                final_capture = true
            }, requestId, cancellationToken);

        return ParseCapture(root);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        var root = await SendJsonAsync(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null, cancellationToken);
        return ParseCapture(root);
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken)
    {
        await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            new { }, requestId, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string paymentReference, string requestId, CancellationToken cancellationToken)
    {
        var root = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new
            {
                amount = Money(amount, currency),
                custom_id = paymentReference
            }, requestId, cancellationToken);
        return ParseRefund(root);
    }

    public async Task<PayPalRefundResult> GetRefundAsync(string refundId, CancellationToken cancellationToken)
    {
        var root = await SendJsonAsync(HttpMethod.Get,
            $"/v2/payments/refunds/{Uri.EscapeDataString(refundId)}", null, null, cancellationToken);
        return ParseRefund(root);
    }

    public async Task<PayPalVaultResult> CreatePaymentTokenAsync(PayPalCard card, string merchantCustomerId,
        string requestId, CancellationToken cancellationToken)
    {
        var root = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens",
            new
            {
                payment_source = new { card = CardBody(card) },
                customer = new { merchant_customer_id = merchantCustomerId }
            }, requestId, cancellationToken);
        ThrowIfChallenge(root);

        var savedCard = root.GetProperty("payment_source").GetProperty("card");
        var customer = OptionalProperty(root, "customer");
        return new PayPalVaultResult(
            RequiredString(root, "id"),
            OptionalString(customer, "id"),
            RequiredString(savedCard, "brand"),
            RequiredString(savedCard, "last_digits"),
            RequiredString(savedCard, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        await SendJsonAsync(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}", null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw new ArgumentException("The reconciliation start must precede the end.");

        var all = new List<PayPalTransaction>();
        var cursor = from.ToUniversalTime();
        var rangeEnd = to.ToUniversalTime();
        while (cursor < rangeEnd)
        {
            var chunkEnd = cursor.AddDays(31) < rangeEnd ? cursor.AddDays(31) : rangeEnd;
            await ReadTransactionChunkAsync(cursor, chunkEnd, all, cancellationToken);
            cursor = chunkEnd;
        }

        return all.DistinctBy(t => new
        {
            t.TransactionId,
            t.EventCode,
            t.InitiatedAt,
            t.Amount,
            t.Currency
        }).ToList();
    }

    private async Task ReadTransactionChunkAsync(DateTimeOffset from, DateTimeOffset to,
        List<PayPalTransaction> target, CancellationToken cancellationToken)
    {
        for (var page = 1; ; page++)
        {
            var path = "/v1/reporting/transactions" +
                       $"?start_date={EncodeDate(from)}&end_date={EncodeDate(to)}" +
                       $"&fields=transaction_info&balance_affecting_records_only=N&page_size={TransactionPageSize}&page={page}";
            var root = await SendJsonAsync(HttpMethod.Get, path, null, null, cancellationToken);
            var details = OptionalProperty(root, "transaction_details");
            var count = 0;
            if (details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    count++;
                    var info = detail.GetProperty("transaction_info");
                    var amount = OptionalProperty(info, "transaction_amount");
                    var fee = OptionalProperty(info, "fee_amount");
                    target.Add(new PayPalTransaction(
                        RequiredString(info, "transaction_id"),
                        OptionalString(info, "paypal_reference_id"),
                        OptionalString(info, "paypal_reference_id_type"),
                        OptionalString(info, "transaction_event_code"),
                        OptionalString(info, "transaction_status"),
                        DateValue(info, "transaction_initiation_date"),
                        DateValue(info, "transaction_updated_date"),
                        OptionalMoneyValue(amount),
                        OptionalString(amount, "currency_code"),
                        OptionalMoneyValue(fee),
                        OptionalString(info, "invoice_id"),
                        OptionalString(info, "custom_field")));
                }
            }

            var totalPages = OptionalInt(root, "total_pages");
            if ((totalPages.HasValue && page >= totalPages.Value) || count < TransactionPageSize) break;
            if (page >= 10_000)
                throw new JsonException("PayPal transaction pagination exceeded the safety limit.");
        }
    }

    private async Task<JsonElement> SendJsonAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken)
    {
        var serializedBody = body is null ? null : JsonSerializer.Serialize(body, JsonOptions);
        var refreshedAfterUnauthorized = false;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var token = await GetAccessTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(method, BuildUrl(path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(requestId)) request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            if (method == HttpMethod.Post) request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (serializedBody is not null)
                request.Content = new StringContent(serializedBody, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshedAfterUnauthorized)
            {
                _accessToken = null;
                refreshedAfterUnauthorized = true;
                continue;
            }

            if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt) + Random.Shared.Next(25, 150)), cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
                throw CreateApiException(response.StatusCode, responseBody);

            if (string.IsNullOrWhiteSpace(responseBody)) return default;
            using var document = JsonDocument.Parse(responseBody);
            return document.RootElement.Clone();
        }

        throw new PayPalApiException(HttpStatusCode.ServiceUnavailable, "RETRY_EXHAUSTED",
            "PayPal did not accept the idempotent request after multiple attempts.", null, null);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl("/v1/oauth2/token"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw CreateApiException(response.StatusCode, responseBody);

            using var document = JsonDocument.Parse(responseBody);
            _accessToken = RequiredString(document.RootElement, "access_token");
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiry) ? expiry.GetInt32() : 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret) ||
            string.IsNullOrWhiteSpace(_options.Environment) || string.IsNullOrWhiteSpace(_options.Currency))
            throw new InvalidOperationException(
                "PayPal configuration is incomplete. Configure PayPal:ClientId, PayPal:ClientSecret, PayPal:Environment, and PayPal:Currency.");
        if (_options.Currency.Length != 3)
            throw new InvalidOperationException("PayPal:Currency must be a three-character ISO-4217 currency code.");
    }

    private string BuildUrl(string path)
    {
        var baseUrl = !string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? _options.BaseUrl!
            : _options.Environment.Equals("sandbox", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.sandbox.paypal.com"
                : _options.Environment.Equals("live", StringComparison.OrdinalIgnoreCase) ||
                  _options.Environment.Equals("production", StringComparison.OrdinalIgnoreCase)
                    ? "https://api-m.paypal.com"
                    : throw new InvalidOperationException("PayPal:Environment must be 'sandbox', 'live', or 'production'.");
        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency.ToUpperInvariant(),
        value = decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("F2", CultureInfo.InvariantCulture)
    };

    private static object CardBody(PayPalCard card) => new
    {
        name = card.Name,
        number = card.Number,
        expiry = card.Expiry,
        security_code = card.SecurityCode,
        billing_address = new
        {
            address_line_1 = card.BillingAddress.AddressLine1,
            address_line_2 = card.BillingAddress.AddressLine2,
            admin_area_2 = card.BillingAddress.City,
            admin_area_1 = card.BillingAddress.State,
            postal_code = card.BillingAddress.PostalCode,
            country_code = card.BillingAddress.CountryCode.ToUpperInvariant()
        }
    };

    private static PayPalAuthorizationResult ParseAuthorization(JsonElement root)
    {
        var amount = root.GetProperty("amount");
        return new PayPalAuthorizationResult(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            MoneyValue(amount),
            RequiredString(amount, "currency_code"),
            DateValue(root, "create_time"),
            DateValue(root, "expiration_time"));
    }

    private static PayPalCaptureResult ParseCapture(JsonElement root)
    {
        var amount = root.GetProperty("amount");
        var breakdown = OptionalProperty(root, "seller_receivable_breakdown");
        return new PayPalCaptureResult(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            MoneyValue(amount),
            RequiredString(amount, "currency_code"),
            MoneyValue(breakdown, "paypal_fee"),
            MoneyValue(breakdown, "net_amount"),
            DateValue(root, "create_time"));
    }

    private static PayPalRefundResult ParseRefund(JsonElement root)
    {
        var amount = root.GetProperty("amount");
        return new PayPalRefundResult(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            MoneyValue(amount),
            RequiredString(amount, "currency_code"),
            DateValue(root, "create_time"));
    }

    private static PayPalApiException CreateApiException(HttpStatusCode statusCode, string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            var issue = root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array &&
                        details.GetArrayLength() > 0
                ? OptionalString(details[0], "issue")
                : null;
            return new PayPalApiException(statusCode,
                OptionalString(root, "name") ?? "API_ERROR",
                OptionalString(root, "message") ?? "The payment processor rejected the request.",
                issue,
                OptionalString(root, "debug_id"));
        }
        catch (JsonException)
        {
            return new PayPalApiException(statusCode, "API_ERROR", "The payment processor returned an unreadable error.", null, null);
        }
    }

    private static void ThrowIfChallenge(JsonElement root)
    {
        if (OptionalString(root, "status") == "PAYER_ACTION_REQUIRED" || ContainsApprovalLink(root))
            throw new PayPalChallengeRequiredException();
    }

    private static bool ContainsApprovalLink(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("rel", out var rel) && rel.ValueKind == JsonValueKind.String &&
                rel.GetString() is "approve" or "payer-action") return true;
            return element.EnumerateObject().Any(property => ContainsApprovalLink(property.Value));
        }
        return element.ValueKind == JsonValueKind.Array && element.EnumerateArray().Any(ContainsApprovalLink);
    }

    private static string EncodeDate(DateTimeOffset value) => Uri.EscapeDataString(
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

    private static JsonElement OptionalProperty(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var property) ? property : default;

    private static string RequiredString(JsonElement element, string name) =>
        OptionalString(element, name) ?? throw new JsonException($"PayPal response omitted '{name}'.");

    private static string? OptionalString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static int? OptionalInt(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var property)) return null;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numericValue)) return numericValue;
        return property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out var stringValue)
            ? stringValue
            : null;
    }

    private static decimal MoneyValue(JsonElement money) =>
        OptionalMoneyValue(money) ?? throw new JsonException("PayPal response omitted a required money value.");

    private static decimal? OptionalMoneyValue(JsonElement money)
    {
        if (money.ValueKind != JsonValueKind.Object || !money.TryGetProperty("value", out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var numericValue)) return numericValue;
        return value.ValueKind == JsonValueKind.String &&
               decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var stringValue)
            ? stringValue
            : null;
    }

    private static decimal? MoneyValue(JsonElement parent, string name)
    {
        var money = OptionalProperty(parent, name);
        return money.ValueKind == JsonValueKind.Object ? MoneyValue(money) : null;
    }

    private static DateTimeOffset? DateValue(JsonElement parent, string name) =>
        DateTimeOffset.TryParse(OptionalString(parent, name), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var value) ? value : null;
}

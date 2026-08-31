using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IPayPalClient
{
    string Currency { get; }
    Task<PayPalOrderResult> CreateOrderAsync(int orderId, string invoiceId, decimal amount, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> AuthorizeAsync(string payPalOrderId, CardInput? card, string? vaultId, string invoiceId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string invoiceId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string invoiceId, CancellationToken cancellationToken);
    Task VoidAsync(string authorizationId, string invoiceId, CancellationToken cancellationToken);
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string invoiceId, string idempotencyKey, string? note, CancellationToken cancellationToken);
    Task<PayPalVaultResult> CreatePaymentTokenAsync(string buyerId, CardInput card, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransaction>> SearchAllTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed class PayPalClient : IPayPalClient
{
    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly string _baseUrl;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _baseUrl = ResolveBaseUrl(_options);
    }

    public string Currency => _options.Currency.ToUpperInvariant();

    // Contract: checkout_orders_v2.json /v2/checkout/orders, operationId CreateOrder.
    public async Task<PayPalOrderResult> CreateOrderAsync(int orderId, string invoiceId, decimal amount, CancellationToken cancellationToken)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = $"eshop-order-{orderId}",
                    custom_id = invoiceId,
                    invoice_id = invoiceId,
                    amount = Money(amount, Currency)
                }
            }
        };
        using var response = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body,
            RequestId($"{invoiceId}-create"), cancellationToken);
        using var json = await ReadSuccessAsync(response, cancellationToken);
        return new PayPalOrderResult(RequiredString(json.RootElement, "id"), RequiredString(json.RootElement, "status"));
    }

    // Contract: checkout_orders_v2.json /v2/checkout/orders/{id}/authorize, operationId AuthorizeOrder.
    public async Task<PayPalAuthorizationResult> AuthorizeAsync(string payPalOrderId, CardInput? card,
        string? vaultId, string invoiceId, CancellationToken cancellationToken)
    {
        object cardSource = card is not null
            ? CardPayload(card)
            : new
            {
                vault_id = vaultId,
                stored_credential = new { payment_initiator = "CUSTOMER", payment_type = "ONE_TIME", usage = "SUBSEQUENT" }
            };
        var body = new { payment_source = new { card = cardSource } };
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize", body,
            RequestId($"{invoiceId}-authorize"), cancellationToken);
        using var json = await ReadSuccessAsync(response, cancellationToken);

        var root = json.RootElement;
        var authorization = root.GetProperty("purchase_units")[0].GetProperty("payments").GetProperty("authorizations")[0];
        return ParseAuthorization(authorization);
    }

    // Contract: payments_payment_v2.json /v2/payments/authorizations/{authorization_id}, operationId GetAuthorizedPayment.
    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null, cancellationToken);
        using var json = await ReadSuccessAsync(response, cancellationToken);
        return ParseAuthorization(json.RootElement);
    }

    // Contract: payments_payment_v2.json .../{authorization_id}/reauthorize, operationId ReauthorizePayment.
    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string invoiceId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = Money(amount, Currency) }, RequestId($"{invoiceId}-reauthorize"), cancellationToken);
        using var json = await ReadSuccessAsync(response, cancellationToken);
        return ParseAuthorization(json.RootElement);
    }

    // Contract: payments_payment_v2.json .../{authorization_id}/capture, operationId CaptureAuthorizedPayment.
    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount,
        string invoiceId, CancellationToken cancellationToken)
    {
        var body = new { amount = Money(amount, Currency), invoice_id = invoiceId, final_capture = true };
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture", body,
            RequestId($"{invoiceId}-capture"), cancellationToken);
        using var json = await ReadSuccessAsync(response, cancellationToken);
        var root = json.RootElement;
        var breakdown = OptionalProperty(root, "seller_receivable_breakdown");
        return new PayPalCaptureResult(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            ReadMoney(root, "amount"),
            breakdown.HasValue ? ReadOptionalMoney(breakdown.Value, "paypal_fee") : null,
            breakdown.HasValue ? ReadOptionalMoney(breakdown.Value, "net_amount") : null,
            ReadDate(root, "create_time"));
    }

    // Contract: payments_payment_v2.json .../{authorization_id}/void, operationId VoidPayment.
    public async Task VoidAsync(string authorizationId, string invoiceId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void", null,
            RequestId($"{invoiceId}-void"), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    // Contract: payments_payment_v2.json /v2/payments/captures/{capture_id}/refund, operationId RefundCapturedPayment.
    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string invoiceId,
        string idempotencyKey, string? note, CancellationToken cancellationToken)
    {
        var body = new
        {
            amount = Money(amount, Currency),
            custom_id = invoiceId,
            invoice_id = invoiceId,
            note_to_payer = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund", body,
            RequestId($"{invoiceId}-refund-{idempotencyKey}"), cancellationToken);
        using var json = await ReadSuccessAsync(response, cancellationToken);
        var root = json.RootElement;
        return new PayPalRefundResult(RequiredString(root, "id"), RequiredString(root, "status"),
            ReadMoney(root, "amount"), ReadDate(root, "create_time"));
    }

    // Contract: vault_payment_tokens_v3.json /v3/vault/payment-tokens, operationId CreatePaymentToken.
    public async Task<PayPalVaultResult> CreatePaymentTokenAsync(string buyerId, CardInput card,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            customer = new { merchant_customer_id = MerchantCustomerId(buyerId) },
            payment_source = new { card = CardPayload(card) }
        };
        using var response = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body,
            RequestId($"vault-{buyerId}-{card.Number}-{card.Expiry}"), cancellationToken);
        using var json = await ReadSuccessAsync(response, cancellationToken);
        var root = json.RootElement;
        var savedCard = root.GetProperty("payment_source").GetProperty("card");
        return new PayPalVaultResult(RequiredString(root, "id"), RequiredString(savedCard, "brand"),
            RequiredString(savedCard, "last_digits"), RequiredString(savedCard, "expiry"));
    }

    // Contract: vault_payment_tokens_v3.json /v3/vault/payment-tokens/{id}, operationId DeletePaymentToken.
    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}", null, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    // Contract: transaction_search_v1.json /v1/reporting/transactions, operationId SearchTransactions.
    public async Task<IReadOnlyList<PayPalTransaction>> SearchAllTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<PayPalTransaction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rangeStart = from;
        while (rangeStart < to)
        {
            var rangeEnd = rangeStart.AddDays(31);
            if (rangeEnd > to) rangeEnd = to;

            var page = 1;
            var totalPages = 1;
            do
            {
                var query = $"?start_date={Uri.EscapeDataString(ReportingDate(rangeStart))}" +
                            $"&end_date={Uri.EscapeDataString(ReportingDate(rangeEnd))}" +
                            "&fields=transaction_info&balance_affecting_records_only=N&page_size=500" +
                            $"&page={page}";
                using var response = await SendAsync(HttpMethod.Get, "/v1/reporting/transactions" + query,
                    null, null, cancellationToken);
                using var json = await ReadSuccessAsync(response, cancellationToken);
                var root = json.RootElement;
                totalPages = root.TryGetProperty("total_pages", out var pages) ? pages.GetInt32() : 1;
                if (root.TryGetProperty("transaction_details", out var details))
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        var info = detail.GetProperty("transaction_info");
                        var id = RequiredString(info, "transaction_id");
                        var discriminator = string.Join('|', id,
                            OptionalString(info, "transaction_event_code"), OptionalString(info, "transaction_updated_date"));
                        if (!seen.Add(discriminator)) continue;
                        results.Add(new PayPalTransaction(id, OptionalString(info, "paypal_reference_id"),
                            OptionalString(info, "paypal_reference_id_type"), OptionalString(info, "invoice_id"),
                            OptionalString(info, "transaction_event_code"), OptionalString(info, "transaction_status"),
                            ReadOptionalMoney(info, "transaction_amount"), ReadOptionalMoney(info, "fee_amount"),
                            ReadDate(info, "transaction_initiation_date")));
                    }
                }
                page++;
            } while (page <= totalPages);

            rangeStart = rangeEnd;
        }
        return results;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, _baseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (requestId is not null) request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;
            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/oauth2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            using var json = await ReadSuccessAsync(response, cancellationToken);
            _accessToken = RequiredString(json.RootElement, "access_token");
            var seconds = json.RootElement.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static async Task<JsonDocument> ReadSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode) await ThrowPayPalErrorAsync(response, cancellationToken);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode) await ThrowPayPalErrorAsync(response, cancellationToken);
    }

    private static async Task ThrowPayPalErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? name = null, message = null, debugId = null, issue = null, description = null;
        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            var root = json.RootElement;
            name = OptionalString(root, "name");
            message = OptionalString(root, "message");
            debugId = OptionalString(root, "debug_id");
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0)
            {
                issue = OptionalString(details[0], "issue");
                description = OptionalString(details[0], "description");
            }
        }
        catch (JsonException) { }

        throw new PayPalApiException(response.StatusCode, name, issue,
            description ?? message ?? "PayPal rejected the request.", debugId);
    }

    private static PayPalAuthorizationResult ParseAuthorization(JsonElement root) => new(
        RequiredString(root, "id"), RequiredString(root, "status"), ReadMoney(root, "amount"),
        ReadDate(root, "create_time"), ReadDate(root, "expiration_time"));

    private static object CardPayload(CardInput card) => new
    {
        name = card.Name,
        number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal),
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

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency,
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static decimal ReadMoney(JsonElement root, string property) =>
        decimal.Parse(root.GetProperty(property).GetProperty("value").GetString()!, CultureInfo.InvariantCulture);

    private static decimal? ReadOptionalMoney(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? decimal.Parse(value.GetProperty("value").GetString()!, CultureInfo.InvariantCulture) : null;

    private static JsonElement? OptionalProperty(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) ? value : null;
    private static string RequiredString(JsonElement root, string property) => root.GetProperty(property).GetString()!;
    private static string? OptionalString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static DateTimeOffset? ReadDate(JsonElement root, string property) =>
        DateTimeOffset.TryParse(OptionalString(root, property), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value) ? value : null;
    private static string MerchantCustomerId(string buyerId) => "eshop-" + Hash(buyerId)[..16];
    private static string ReportingDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    private static string RequestId(string source) => "eshop-" + Hash(source)[..32];
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string ResolveBaseUrl(PayPalOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl)) return options.BaseUrl.TrimEnd('/');
        return options.Environment.Trim().ToLowerInvariant() switch
        {
            "sandbox" => "https://api-m.sandbox.paypal.com",
            "live" or "production" => "https://api-m.paypal.com",
            _ => throw new InvalidOperationException("PayPal:Environment must be Sandbox, Live, or Production.")
        };
    }
}

public sealed record BillingAddressInput(string AddressLine1, string? AddressLine2, string City,
    string State, string PostalCode, string CountryCode);

public sealed record CardInput(string Name, string Number, string Expiry, string SecurityCode,
    BillingAddressInput BillingAddress)
{
    public string LastFour
    {
        get
        {
            var normalized = Number.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
            return normalized.Length >= 4 ? normalized[^4..] : normalized;
        }
    }
}

public sealed record PayPalOrderResult(string Id, string Status);
public sealed record PayPalAuthorizationResult(string Id, string Status, decimal Amount, DateTimeOffset? CreatedAt, DateTimeOffset? ExpiresAt);
public sealed record PayPalCaptureResult(string Id, string Status, decimal Amount, decimal? Fee, decimal? Net, DateTimeOffset? CreatedAt);
public sealed record PayPalRefundResult(string Id, string Status, decimal Amount, DateTimeOffset? CreatedAt);
public sealed record PayPalVaultResult(string Id, string Brand, string LastDigits, string Expiry);
public sealed record PayPalTransaction(string Id, string? ReferenceId, string? ReferenceType, string? InvoiceId,
    string? EventCode, string? Status, decimal? Amount, decimal? Fee, DateTimeOffset? InitiatedAt);

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string? name, string? issue, string message, string? debugId)
        : base(message)
    {
        StatusCode = statusCode;
        Name = name;
        Issue = issue;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string? Name { get; }
    public string? Issue { get; }
    public string? DebugId { get; }
    public bool RequiresPayerAction => Name == "PAYER_ACTION_REQUIRED" || Issue == "PAYER_ACTION_REQUIRED";
}

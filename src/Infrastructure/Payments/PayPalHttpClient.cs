using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalHttpClient : IPayPalClient
{
    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalHttpClient(HttpClient httpClient, IOptions<PayPalOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<PayPalOrderResult> CreateOrderAsync(int orderId, decimal amount, string currency, string requestId, CancellationToken cancellationToken)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = $"order-{orderId}",
                    custom_id = orderId.ToString(CultureInfo.InvariantCulture),
                    invoice_id = $"ESHOP-{orderId}",
                    amount = Money(amount, currency)
                }
            }
        };
        using var document = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId, cancellationToken);
        return new PayPalOrderResult(RequiredString(document.RootElement, "id"), RequiredString(document.RootElement, "status"));
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string paypalOrderId, CardDetails? card, string? vaultId, string requestId, CancellationToken cancellationToken)
    {
        object cardSource = card is not null ? CardPayload(card) : new { vault_id = vaultId };
        var body = new { payment_source = new { card = cardSource } };
        using var document = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{Uri.EscapeDataString(paypalOrderId)}/authorize", body, requestId, cancellationToken);
        var root = document.RootElement;
        if (root.TryGetProperty("status", out var status) && status.GetString() == "PAYER_ACTION_REQUIRED")
            throw new PayPalApiException(System.Net.HttpStatusCode.Conflict, "PAYER_ACTION_REQUIRED", "PayPal requires browser approval for this card payment; this headless flow cannot continue.", null);
        var authorization = root.GetProperty("purchase_units")[0].GetProperty("payments").GetProperty("authorizations")[0];
        return ParseAuthorization(authorization);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, string requestId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize", new { }, requestId, cancellationToken);
        return ParseAuthorization(document.RootElement);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken)
    {
        var body = new { amount = Money(amount, currency), final_capture = true };
        using var document = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture", body, requestId, cancellationToken);
        var root = document.RootElement;
        var breakdown = root.TryGetProperty("seller_receivable_breakdown", out var value) ? value : default;
        return ParseCapture(root);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get, $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null, cancellationToken);
        return ParseCapture(document.RootElement);
    }

    public async Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void", new { }, requestId, cancellationToken, allowEmpty: true);
        return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("status", out var status)
            ? status.GetString() ?? "VOIDED"
            : "VOIDED";
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency, string requestId, CancellationToken cancellationToken)
    {
        var body = new { amount = Money(amount, currency) };
        using var document = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund", body, requestId, cancellationToken);
        var root = document.RootElement;
        return new PayPalRefundResult(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            MoneyValue(root.GetProperty("amount")),
            OptionalDate(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task<PayPalVaultResult> VaultCardAsync(string buyerId, string? customerId, CardDetails card, string requestId, CancellationToken cancellationToken)
    {
        object setupBody = customerId is null
            ? new { payment_source = new { card = CardPayload(card, includeSecurityCode: true) } }
            : new { customer = new { id = customerId }, payment_source = new { card = CardPayload(card, includeSecurityCode: true) } };
        using var setup = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody, requestId + "-s", cancellationToken);
        var setupRoot = setup.RootElement;
        var setupStatus = RequiredString(setupRoot, "status");
        if (setupStatus == "PAYER_ACTION_REQUIRED")
            throw new PayPalApiException(System.Net.HttpStatusCode.Conflict, "PAYER_ACTION_REQUIRED", "PayPal requires browser approval to save this card; this headless flow cannot continue.", null);
        if (setupStatus != "APPROVED")
            throw new PayPalApiException(System.Net.HttpStatusCode.UnprocessableEntity, "SETUP_TOKEN_NOT_APPROVED", $"PayPal returned setup-token status {setupStatus}.", null);

        var setupTokenId = RequiredString(setupRoot, "id");
        var resolvedCustomerId = RequiredString(setupRoot.GetProperty("customer"), "id");
        var confirmBody = new { payment_source = new { token = new { id = setupTokenId, type = "SETUP_TOKEN" } } };
        using var token = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", confirmBody, requestId + "-p", cancellationToken);
        var tokenRoot = token.RootElement;
        var tokenCard = tokenRoot.GetProperty("payment_source").GetProperty("card");
        return new PayPalVaultResult(
            RequiredString(tokenRoot, "id"),
            tokenRoot.TryGetProperty("customer", out var customer) ? RequiredString(customer, "id") : resolvedCustomerId,
            RequiredString(tokenCard, "brand"),
            RequiredString(tokenCard, "last_digits"),
            RequiredString(tokenCard, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{Uri.EscapeDataString(tokenId)}", null, null, cancellationToken, allowEmpty: true);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var result = new List<PayPalTransaction>();
        var cursor = from.ToUniversalTime();
        var final = to.ToUniversalTime();
        while (cursor < final)
        {
            var chunkEnd = cursor.AddDays(31) < final ? cursor.AddDays(31) : final;
            var page = 1;
            var totalPages = 1;
            while (page <= totalPages)
            {
                var path = $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(cursor.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}&end_date={Uri.EscapeDataString(chunkEnd.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}&fields=transaction_info&balance_affecting_records_only=N&page_size=500&page={page}";
                using var document = await SendAsync(HttpMethod.Get, path, null, null, cancellationToken);
                var root = document.RootElement;
                totalPages = root.TryGetProperty("total_pages", out var pages) ? Math.Max(1, pages.GetInt32()) : 1;
                if (root.TryGetProperty("transaction_details", out var details))
                {
                    foreach (var detail in details.EnumerateArray()) result.Add(ParseTransaction(detail.GetProperty("transaction_info")));
                }
                page++;
            }
            cursor = chunkEnd;
        }
        return result.GroupBy(x => new { x.TransactionId, x.EventCode, x.InitiationDate }).Select(x => x.First()).ToList();
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, string? requestId, CancellationToken cancellationToken, bool allowEmpty = false)
    {
        using var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (path.StartsWith("/v1/reporting/", StringComparison.Ordinal)) request.Headers.TryAddWithoutValidation("PayPal-Enforce-ISO8601-Format", "true");
        if (!string.IsNullOrWhiteSpace(requestId)) request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        if (body is not null) request.Content = JsonContent.Create(body);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw ParseError(response.StatusCode, content);
        if (string.IsNullOrWhiteSpace(content))
        {
            if (!allowEmpty) throw new PayPalApiException(response.StatusCode, "EMPTY_RESPONSE", "PayPal returned an empty response unexpectedly.", null);
            return JsonDocument.Parse("{}");
        }
        return JsonDocument.Parse(content);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw ParseError(response.StatusCode, content);
            using var document = JsonDocument.Parse(content);
            _accessToken = RequiredString(document.RootElement, "access_token");
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private Uri BuildUri(string path) => new(_options.ResolveBaseUrl().TrimEnd('/') + path, UriKind.Absolute);
    private static object Money(decimal amount, string currency) => new { currency_code = currency, value = amount.ToString("0.00", CultureInfo.InvariantCulture) };

    private static object CardPayload(CardDetails card, bool includeSecurityCode = true)
    {
        var address = new
        {
            address_line_1 = card.BillingAddress.AddressLine1,
            address_line_2 = card.BillingAddress.AddressLine2,
            admin_area_2 = card.BillingAddress.City,
            admin_area_1 = card.BillingAddress.State,
            postal_code = card.BillingAddress.PostalCode,
            country_code = card.BillingAddress.CountryCode
        };
        return includeSecurityCode
            ? new { number = card.Number, expiry = card.Expiry, security_code = card.SecurityCode, name = card.Name, billing_address = address }
            : new { number = card.Number, expiry = card.Expiry, name = card.Name, billing_address = address };
    }

    private static PayPalAuthorizationResult ParseAuthorization(JsonElement value) => new(
        RequiredString(value, "id"),
        RequiredString(value, "status"),
        MoneyValue(value.GetProperty("amount")),
        OptionalDate(value, "create_time") ?? DateTimeOffset.UtcNow,
        OptionalDate(value, "expiration_time"));

    private static PayPalCaptureResult ParseCapture(JsonElement root)
    {
        var breakdown = root.TryGetProperty("seller_receivable_breakdown", out var value) ? value : default;
        return new PayPalCaptureResult(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            MoneyValue(root.GetProperty("amount")),
            OptionalMoneyValue(breakdown, "paypal_fee"),
            OptionalMoneyValue(breakdown, "net_amount"),
            OptionalDate(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    private static PayPalTransaction ParseTransaction(JsonElement value)
    {
        decimal? amount = value.TryGetProperty("transaction_amount", out var money) ? MoneyValue(money) : null;
        string? currency = value.TryGetProperty("transaction_amount", out money) && money.TryGetProperty("currency_code", out var code) ? code.GetString() : null;
        return new PayPalTransaction(
            RequiredString(value, "transaction_id"),
            OptionalString(value, "paypal_reference_id"),
            OptionalString(value, "invoice_id"),
            OptionalString(value, "transaction_status") ?? "UNKNOWN",
            OptionalString(value, "transaction_event_code") ?? "UNKNOWN",
            amount,
            currency,
            OptionalDate(value, "transaction_initiation_date"),
            OptionalDate(value, "transaction_updated_date"));
    }

    private static PayPalApiException ParseError(System.Net.HttpStatusCode statusCode, string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            var issue = OptionalString(root, "name") ?? "PAYPAL_ERROR";
            var message = OptionalString(root, "message") ?? "PayPal rejected the request.";
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0)
            {
                issue = OptionalString(details[0], "issue") ?? issue;
                message = OptionalString(details[0], "description") ?? message;
            }
            return new PayPalApiException(statusCode, issue, message, OptionalString(root, "debug_id"));
        }
        catch (JsonException)
        {
            return new PayPalApiException(statusCode, "PAYPAL_ERROR", "PayPal rejected the request.", null);
        }
    }

    private static string RequiredString(JsonElement value, string property) => value.GetProperty(property).GetString() ?? throw new JsonException($"PayPal response omitted {property}.");
    private static string? OptionalString(JsonElement value, string property) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var item) ? item.GetString() : null;
    private static decimal MoneyValue(JsonElement money) => decimal.Parse(RequiredString(money, "value"), NumberStyles.Number, CultureInfo.InvariantCulture);
    private static decimal? OptionalMoneyValue(JsonElement value, string property) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var money) ? MoneyValue(money) : null;
    private static DateTimeOffset? OptionalDate(JsonElement value, string property) => OptionalString(value, property) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
}

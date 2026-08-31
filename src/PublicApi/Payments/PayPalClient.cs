using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(45);
        _options = options.Value;
    }

    public async Task<PayPalOrderResult> CreateOrderAsync(string reference, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
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
        var json = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId, cancellationToken);
        return new PayPalOrderResult(RequiredString(json, "id"), RequiredString(json, "status"));
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardAsync(string orderId, CardInput card,
        string requestId, CancellationToken cancellationToken)
    {
        var body = new
        {
            payment_source = new
            {
                card = new
                {
                    name = card.Name,
                    number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    billing_address = BillingAddress(card.BillingAddress)
                }
            }
        };
        return AuthorizeAsync(orderId, body, requestId, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(string orderId, string vaultId,
        string requestId, CancellationToken cancellationToken)
    {
        var body = new
        {
            payment_source = new
            {
                card = new
                {
                    vault_id = vaultId,
                    stored_credential = new
                    {
                        payment_initiator = "CUSTOMER",
                        payment_type = "ONE_TIME",
                        usage = "SUBSEQUENT"
                    }
                }
            }
        };
        return AuthorizeAsync(orderId, body, requestId, cancellationToken);
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(string orderId, object body,
        string requestId, CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}/authorize",
            body, requestId, cancellationToken);
        var authorization = json["purchase_units"]?[0]?["payments"]?["authorizations"]?[0]
            ?? throw InvalidPayPalResponse("authorization");
        var card = json["payment_source"]?["card"];
        return ParseAuthorization(authorization, card, HasPayerAction(json));
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null, cancellationToken);
        return ParseAuthorization(json, null, false);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId,
        decimal amount, string currency, string requestId, CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = Money(amount, currency) }, requestId, cancellationToken);
        return ParseAuthorization(json, null, false);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount,
        string currency, string invoiceId, string requestId, CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new { amount = Money(amount, currency), invoice_id = invoiceId, final_capture = true },
            requestId, cancellationToken);
        return ParseCapture(json);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId,
        CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null, cancellationToken);
        return ParseCapture(json);
    }

    private static PayPalCaptureResult ParseCapture(JsonNode json)
    {
        var breakdown = json["seller_receivable_breakdown"];
        var capturedAmount = Decimal(json["amount"]?["value"]);
        var fee = DecimalOrZero(breakdown?["paypal_fee"]?["value"]);
        var net = DecimalOrDefault(breakdown?["net_amount"]?["value"], capturedAmount - fee);
        return new PayPalCaptureResult(RequiredString(json, "id"), RequiredString(json, "status"),
            capturedAmount, RequiredString(json["amount"], "currency_code"), fee, net,
            Date(json["create_time"]) ?? DateTimeOffset.UtcNow);
    }

    public async Task<string> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            null, requestId, cancellationToken);
        return json["status"]?.GetValue<string>() ?? "VOIDED";
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount,
        string currency, string customId, string? note, string requestId,
        CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new { amount = Money(amount, currency), custom_id = customId, note_to_payer = note },
            requestId, cancellationToken);
        return new PayPalRefundResult(RequiredString(json, "id"), RequiredString(json, "status"),
            Decimal(json["amount"]?["value"]), RequiredString(json["amount"], "currency_code"),
            Date(json["create_time"]) ?? DateTimeOffset.UtcNow);
    }

    public async Task<PayPalVaultResult> SaveCardAsync(CardInput card, string merchantCustomerId,
        string requestId, CancellationToken cancellationToken)
    {
        var setupBody = new
        {
            customer = new { merchant_customer_id = merchantCustomerId },
            payment_source = new
            {
                card = new
                {
                    name = card.Name,
                    number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    billing_address = BillingAddress(card.BillingAddress)
                }
            }
        };
        var setup = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody,
            requestId + "-setup", cancellationToken);
        var status = RequiredString(setup, "status");
        if (status == "PAYER_ACTION_REQUIRED" || HasPayerAction(setup))
            return ParseVault(setup, status, true);
        if (status != "APPROVED")
            throw new PaymentOperationException(409,
                $"PayPal did not approve the card for vaulting (status: {status}).");

        var setupId = RequiredString(setup, "id");
        var tokenBody = new
        {
            payment_source = new { token = new { id = setupId, type = "SETUP_TOKEN" } }
        };
        var token = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenBody,
            requestId + "-token", cancellationToken);
        return ParseVault(token, "VAULTED", false);
    }

    public async Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{Uri.EscapeDataString(tokenId)}",
            null, null, cancellationToken);
    }

    public async Task<PayPalTransactionPage> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, int page, int pageSize, CancellationToken cancellationToken)
    {
        const string PayPalDateFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
        var query = $"start_date={Uri.EscapeDataString(from.UtcDateTime.ToString(PayPalDateFormat, CultureInfo.InvariantCulture))}" +
                    $"&end_date={Uri.EscapeDataString(to.UtcDateTime.ToString(PayPalDateFormat, CultureInfo.InvariantCulture))}" +
                    $"&fields=transaction_info&page_size={pageSize}&page={page}";
        var json = await SendAsync(HttpMethod.Get, "/v1/reporting/transactions?" + query,
            null, null, cancellationToken);
        var transactions = new List<PayPalTransactionResult>();
        if (json["transaction_details"] is JsonArray details)
        {
            foreach (var detail in details)
            {
                var info = detail?["transaction_info"];
                if (info is null) continue;
                transactions.Add(new PayPalTransactionResult(
                    RequiredString(info, "transaction_id"),
                    String(info["paypal_reference_id"]), String(info["transaction_event_code"]),
                    String(info["transaction_status"]), Date(info["transaction_initiation_date"]),
                    DecimalOrNull(info["transaction_amount"]?["value"]),
                    String(info["transaction_amount"]?["currency_code"]),
                    DecimalOrNull(info["fee_amount"]?["value"]), String(info["invoice_id"]),
                    String(info["custom_field"])));
            }
        }
        return new PayPalTransactionPage(transactions,
            json["page"]?.GetValue<int>() ?? page, json["total_pages"]?.GetValue<int>() ?? page);
    }

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken)
    {
        var serializedBody = body is null ? null : JsonSerializer.Serialize(body, JsonOptions);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var token = await GetAccessTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(method, BuildUri(path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (requestId is not null) request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            if (serializedBody is not null)
                request.Content = new StringContent(serializedBody, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _accessToken = null;
                continue;
            }
            if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)), cancellationToken);
                continue;
            }

            var content = response.Content is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw CreatePayPalException(response.StatusCode, content);
            if (string.IsNullOrWhiteSpace(content)) return new JsonObject();
            return JsonNode.Parse(content) ?? throw InvalidPayPalResponse("JSON body");
        }
        throw new PaymentOperationException(503, "PayPal did not respond after safe retries.");
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
            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
                throw new PaymentOperationException(503, "PayPal credentials are not configured.");
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8,
                "application/x-www-form-urlencoded");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw CreatePayPalException(response.StatusCode, content);
            var json = JsonNode.Parse(content) ?? throw InvalidPayPalResponse("OAuth response");
            _accessToken = RequiredString(json, "access_token");
            var expiresIn = json["expires_in"]?.GetValue<int>() ?? 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private Uri BuildUri(string path) => new(_options.ResolveBaseUrl().TrimEnd('/') + "/" + path.TrimStart('/'));

    private static PayPalAuthorizationResult ParseAuthorization(JsonNode json, JsonNode? card,
        bool requiresPayerAction) => new(
        RequiredString(json, "id"), RequiredString(json, "status"),
        Decimal(json["amount"]?["value"]), RequiredString(json["amount"], "currency_code"),
        Date(json["create_time"]) ?? DateTimeOffset.UtcNow, Date(json["expiration_time"]),
        String(card?["brand"]), String(card?["last_digits"]), requiresPayerAction,
        String(json["supplementary_data"]?["related_ids"]?["capture_id"]));

    private static PayPalVaultResult ParseVault(JsonNode json, string status, bool requiresAction)
    {
        var card = json["payment_source"]?["card"] ?? throw InvalidPayPalResponse("vault card");
        return new PayPalVaultResult(RequiredString(json, "id"), String(json["customer"]?["id"]), status,
            String(card["brand"]) ?? "UNKNOWN", String(card["last_digits"]) ?? string.Empty,
            String(card["expiry"]), requiresAction);
    }

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency.ToUpperInvariant(),
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static object BillingAddress(BillingAddressInput address) => new
    {
        country_code = address.CountryCode.ToUpperInvariant(), address_line_1 = address.AddressLine1,
        address_line_2 = address.AddressLine2, admin_area_2 = address.City,
        admin_area_1 = address.State, postal_code = address.PostalCode
    };

    private static bool HasPayerAction(JsonNode json) =>
        String(json["status"]) == "PAYER_ACTION_REQUIRED" ||
        (json["links"] as JsonArray)?.Any(x =>
            String(x?["rel"]) is "payer-action" or "approve") == true;

    private static PaymentOperationException CreatePayPalException(HttpStatusCode status, string body)
    {
        string? name = null, message = null, debugId = null, issue = null;
        try
        {
            var json = JsonNode.Parse(body);
            name = String(json?["name"]);
            message = String(json?["message"]);
            debugId = String(json?["debug_id"]);
            issue = String(json?["details"]?[0]?["issue"]);
        }
        catch (JsonException) { }
        var safeMessage = $"PayPal rejected the operation ({name ?? status.ToString()}{(issue is null ? string.Empty : $": {issue}")}).";
        if (!string.IsNullOrWhiteSpace(message) && message.Length <= 300) safeMessage += " " + message;
        var responseStatus = (int)status >= 500 || status == HttpStatusCode.TooManyRequests
            ? 503
            : status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden ? 502 : (int)status;
        return new PaymentOperationException(responseStatus, safeMessage, debugId, issue);
    }

    private static PaymentOperationException InvalidPayPalResponse(string field) =>
        new(502, $"PayPal returned a response without the expected {field}.");
    private static string RequiredString(JsonNode? json, string property) =>
        String(json?[property]) ?? throw InvalidPayPalResponse(property);
    private static string? String(JsonNode? value) => value?.GetValue<string>();
    private static DateTimeOffset? Date(JsonNode? value) => DateTimeOffset.TryParse(String(value),
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result) ? result : null;
    private static decimal Decimal(JsonNode? value) => DecimalOrNull(value) ?? throw InvalidPayPalResponse("amount");
    private static decimal DecimalOrZero(JsonNode? value) => DecimalOrNull(value) ?? 0m;
    private static decimal DecimalOrDefault(JsonNode? value, decimal fallback) => DecimalOrNull(value) ?? fallback;
    private static decimal? DecimalOrNull(JsonNode? value) => decimal.TryParse(String(value),
        NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : null;
}

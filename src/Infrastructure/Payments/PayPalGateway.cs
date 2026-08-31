using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalGateway : IPayPalGateway
{
    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalGateway(HttpClient httpClient, IOptions<PayPalOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(string externalReference, decimal amount,
        string currency, PayPalPaymentSource source, CancellationToken cancellationToken)
    {
        var card = source.Card is not null ? CardJson(source.Card) : new JsonObject
        {
            ["vault_id"] = source.VaultId,
            ["stored_credential"] = new JsonObject
            {
                ["payment_initiator"] = "CUSTOMER",
                ["payment_type"] = "ONE_TIME",
                ["usage"] = "SUBSEQUENT"
            }
        };
        var body = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray(new JsonObject
            {
                ["reference_id"] = "default",
                ["custom_id"] = externalReference,
                ["invoice_id"] = externalReference,
                ["amount"] = Money(currency, amount)
            }),
            ["payment_source"] = new JsonObject { ["card"] = card }
        };

        var order = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", body,
            $"order-{externalReference}", cancellationToken);
        var orderId = RequiredString(order, "id");
        var createStatus = String(order, "status") ?? "CREATED";
        EnsureNoPayerAction(createStatus, "card authorization");

        var singleStepAuthorization = First(order, "purchase_units", "payments", "authorizations");
        if (singleStepAuthorization is not null)
            return AuthorizationResult(orderId, createStatus, singleStepAuthorization);

        var authorized = await SendJsonAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}/authorize", new JsonObject(),
            $"authorize-{externalReference}", cancellationToken);
        var orderStatus = String(authorized, "status") ?? createStatus;
        EnsureNoPayerAction(orderStatus, "card authorization");
        var authorization = First(authorized, "purchase_units", "payments", "authorizations")
            ?? throw new PayPalApiException(502, "INVALID_RESPONSE", "Authorization response did not contain an authorization.", null);

        return AuthorizationResult(orderId, orderStatus, authorization);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        var json = await SendJsonAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null, cancellationToken);
        return AuthorizationDetails(json);
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(string externalReference,
        string authorizationId, decimal amount, string currency, CancellationToken cancellationToken)
    {
        var json = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new JsonObject { ["amount"] = Money(currency, amount) }, $"reauthorize-{externalReference}",
            cancellationToken);
        return AuthorizationDetails(json);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string externalReference, string authorizationId,
        decimal amount, string currency, CancellationToken cancellationToken)
    {
        var json = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new JsonObject { ["amount"] = Money(currency, amount), ["final_capture"] = true,
                ["invoice_id"] = externalReference }, $"capture-{externalReference}", cancellationToken);
        var breakdown = json["seller_receivable_breakdown"];
        return new PayPalCaptureResult(RequiredString(json, "id"), RequiredString(json, "status"),
            MoneyValue(json["amount"]), RequiredString(json["amount"], "currency_code"),
            MoneyValue(breakdown?["paypal_fee"]), MoneyValue(breakdown?["net_amount"]),
            Date(json, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task VoidAsync(string externalReference, string authorizationId,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void", null,
            $"void-{externalReference}", cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundAsync(string requestId, string captureId, decimal amount,
        string currency, CancellationToken cancellationToken)
    {
        var json = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new JsonObject { ["amount"] = Money(currency, amount) }, requestId, cancellationToken);
        var breakdown = json["seller_payable_breakdown"];
        return new PayPalRefundResult(RequiredString(json, "id"), RequiredString(json, "status"),
            MoneyValue(json["amount"]), RequiredString(json["amount"], "currency_code"),
            NullableMoneyValue(breakdown?["paypal_fee"]), NullableMoneyValue(breakdown?["net_amount"]),
            NullableMoneyValue(breakdown?["total_refunded_amount"]),
            Date(json, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task<PayPalVaultResult> SaveCardAsync(string requestId, string customerId,
        PaymentCardData card, CancellationToken cancellationToken)
    {
        var setup = await SendJsonAsync(HttpMethod.Post, "/v3/vault/setup-tokens", new JsonObject
        {
            ["customer"] = new JsonObject { ["merchant_customer_id"] = customerId },
            ["payment_source"] = new JsonObject { ["card"] = CardJson(card) }
        }, $"setup-{requestId}", cancellationToken);
        EnsureNoPayerAction(String(setup, "status"), "saving this card");
        var setupId = RequiredString(setup, "id");

        var token = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens", new JsonObject
        {
            ["payment_source"] = new JsonObject
            {
                ["token"] = new JsonObject { ["id"] = setupId, ["type"] = "SETUP_TOKEN" }
            }
        }, $"token-{requestId}", cancellationToken);
        var resultCard = token["payment_source"]?["card"];
        return new PayPalVaultResult(RequiredString(token, "id"),
            RequiredString(resultCard, "brand"), RequiredString(resultCard, "last_digits"),
            RequiredString(resultCard, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{Uri.EscapeDataString(tokenId)}",
            null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<PayPalTransaction>();
        var page = 1;
        while (true)
        {
            var query = $"?start_date={Uri.EscapeDataString(from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}" +
                        $"&end_date={Uri.EscapeDataString(to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}" +
                        $"&fields=transaction_info&page_size=500&page={page}";
            var json = await SendJsonAsync(HttpMethod.Get, "/v1/reporting/transactions" + query,
                null, null, cancellationToken);
            var pageItemCount = 0;
            if (json["transaction_details"] is JsonArray details)
            {
                pageItemCount = details.Count;
                foreach (var detail in details)
                {
                    var info = detail?["transaction_info"];
                    if (info is null) continue;
                    var amountNode = info["transaction_amount"];
                    var feeNode = info["fee_amount"];
                    results.Add(new PayPalTransaction(RequiredString(info, "transaction_id"),
                        String(info, "paypal_reference_id"), String(info, "paypal_reference_id_type"),
                        String(info, "invoice_id"), String(info, "transaction_event_code"),
                        String(info, "transaction_status"), NullableMoneyValue(amountNode),
                        NullableMoneyValue(feeNode), String(amountNode, "currency_code"),
                        Date(info, "transaction_initiation_date"), Date(info, "transaction_updated_date")));
                }
            }
            var totalPages = json["total_pages"]?.GetValue<int>() ?? 0;
            if (totalPages > 0 ? page >= totalPages : pageItemCount < 500) break;
            page++;
        }
        return results;
    }

    private async Task<JsonNode> SendJsonAsync(HttpMethod method, string path, JsonNode? body,
        string? requestId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, body, requestId, cancellationToken);
        var json = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: cancellationToken);
        return json ?? new JsonObject();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonNode? body,
        string? requestId, CancellationToken cancellationToken)
    {
        _options.EnsureConfigured();
        var request = new HttpRequestMessage(method, _options.GetApiBaseUrl().TrimEnd('/') + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (requestId is not null) request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (body is not null) request.Content = JsonContent.Create(body);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return response;

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonNode? error = null;
        try { error = JsonNode.Parse(errorBody); } catch (JsonException) { }
        var detail = String(error, "message") ?? response.ReasonPhrase ?? "PayPal request failed.";
        if (error?["details"] is JsonArray details && details.Count > 0)
        {
            var issue = String(details[0], "issue");
            var description = String(details[0], "description");
            detail = string.Join(": ", new[] { issue, description }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
        response.Dispose();
        throw new PayPalApiException((int)response.StatusCode, String(error, "name") ?? "API_ERROR",
            detail, String(error, "debug_id"));
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
            _options.EnsureConfigured();
            using var request = new HttpRequestMessage(HttpMethod.Post,
                _options.GetApiBaseUrl().TrimEnd('/') + "/v1/oauth2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new PayPalApiException((int)response.StatusCode, "AUTHENTICATION_FAILURE",
                    "PayPal rejected the configured client credentials.", null);
            var json = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: cancellationToken)
                ?? throw new PayPalApiException(502, "INVALID_RESPONSE", "Token response was empty.", null);
            _accessToken = RequiredString(json, "access_token");
            var expiresIn = json["expires_in"]?.GetValue<int>() ?? 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally { _tokenLock.Release(); }
    }

    private static JsonObject CardJson(PaymentCardData card) => new()
    {
        ["name"] = card.Name,
        ["number"] = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
        ["expiry"] = card.Expiry,
        ["security_code"] = card.SecurityCode,
        ["billing_address"] = new JsonObject
        {
            ["address_line_1"] = card.BillingAddress.AddressLine1,
            ["address_line_2"] = card.BillingAddress.AddressLine2,
            ["admin_area_2"] = card.BillingAddress.City,
            ["admin_area_1"] = card.BillingAddress.State,
            ["postal_code"] = card.BillingAddress.PostalCode,
            ["country_code"] = card.BillingAddress.CountryCode.ToUpperInvariant()
        }
    };

    private static JsonObject Money(string currency, decimal value) => new()
    {
        ["currency_code"] = currency.ToUpperInvariant(),
        ["value"] = value.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static PayPalAuthorizationResult AuthorizationResult(string orderId, string orderStatus, JsonNode node)
    {
        var details = AuthorizationDetails(node);
        return new PayPalAuthorizationResult(orderId, orderStatus, details.AuthorizationId, details.Status,
            details.Amount, details.Currency, details.CreatedAt, details.ExpiresAt);
    }

    private static PayPalAuthorizationDetails AuthorizationDetails(JsonNode node) => new(
        RequiredString(node, "id"), RequiredString(node, "status"), MoneyValue(node["amount"]),
        RequiredString(node["amount"], "currency_code"), Date(node, "create_time") ?? DateTimeOffset.UtcNow,
        Date(node, "expiration_time"));

    private static JsonNode? First(JsonNode node, string arrayName, string objectName, string nestedArrayName) =>
        node[arrayName] is JsonArray firstArray && firstArray.Count > 0 &&
        firstArray[0]?[objectName]?[nestedArrayName] is JsonArray nested && nested.Count > 0 ? nested[0] : null;

    private static string RequiredString(JsonNode? node, string property) =>
        String(node, property) ?? throw new PayPalApiException(502, "INVALID_RESPONSE",
            $"PayPal response omitted required field '{property}'.", null);

    private static string? String(JsonNode? node, string property) => node?[property]?.GetValue<string>();
    private static decimal MoneyValue(JsonNode? node) => NullableMoneyValue(node) ?? 0m;
    private static decimal? NullableMoneyValue(JsonNode? node) => decimal.TryParse(String(node, "value"),
        NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : null;
    private static DateTimeOffset? Date(JsonNode? node, string property) => DateTimeOffset.TryParse(
        String(node, property), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result) ? result : null;
    private static void EnsureNoPayerAction(string? status, string operation)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            throw new PayerActionRequiredException(operation);
    }
}

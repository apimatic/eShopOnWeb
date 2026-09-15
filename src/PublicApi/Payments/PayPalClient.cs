using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Hand-written client whose paths, headers and JSON shapes come from the OpenAPI documents
/// under api-specs/paypal (Checkout Orders v2, Payments v2, Vault v3 and Transaction Search v1).
/// </summary>
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
        _options = options.Value;
    }

    public async Task<PayPalAuthorization> AuthorizeOrderAsync(string paymentReference, decimal amount,
        string currency, CardInput? card, string? vaultId, string requestId,
        CancellationToken cancellationToken)
    {
        var source = CardSource(card, vaultId);
        var payload = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["reference_id"] = paymentReference,
                    ["invoice_id"] = paymentReference,
                    ["custom_id"] = paymentReference,
                    ["amount"] = Money(amount, currency)
                }
            },
            ["payment_source"] = new JsonObject { ["card"] = source.DeepClone() }
        };

        var root = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", payload,
            requestId, cancellationToken, preferRepresentation: true);
        var authorization = ParseAuthorization(root!);
        if (authorization is not null) return authorization;

        if (RequiresPayerAction(root!)) throw new PayPalPayerActionRequiredException();

        var paypalOrderId = RequiredString(root!, "id");
        var authorizePayload = new JsonObject
        {
            ["payment_source"] = new JsonObject { ["card"] = source }
        };
        root = await SendJsonAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(paypalOrderId)}/authorize",
            authorizePayload, requestId + "-complete", cancellationToken,
            preferRepresentation: true);
        authorization = ParseAuthorization(root!);
        if (authorization is not null) return authorization;
        if (RequiresPayerAction(root!)) throw new PayPalPayerActionRequiredException();

        throw new PayPalApiException(422, "AUTHORIZATION_NOT_CREATED",
            "PayPal did not return an authorization for the order.", null, Array.Empty<string>());
    }

    public async Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        var root = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new JsonObject { ["amount"] = Money(amount, currency) }, requestId, cancellationToken,
            preferRepresentation: true);
        return ParseStandaloneAuthorization(root!);
    }

    public async Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        var root = await SendJsonAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null,
            cancellationToken);
        return ParseStandaloneAuthorization(root!);
    }

    public async Task<PayPalCapture> CaptureAsync(string authorizationId, decimal amount,
        string currency, string invoiceId, string requestId, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["amount"] = Money(amount, currency),
            ["invoice_id"] = invoiceId,
            ["final_capture"] = true
        };
        var root = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            payload, requestId, cancellationToken, preferRepresentation: true);

        return ParseCapture(root!);
    }

    public async Task<PayPalCapture> GetCaptureAsync(string captureId,
        CancellationToken cancellationToken)
    {
        var root = await SendJsonAsync(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null,
            cancellationToken);
        return ParseCapture(root!);
    }

    public async Task<string> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        var root = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            null, requestId, cancellationToken, allowEmpty: true, preferRepresentation: true);
        return root?["status"]?.GetValue<string>() ?? "VOIDED";
    }

    public async Task<PayPalRefund> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, string customId, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["amount"] = Money(amount, currency),
            ["custom_id"] = customId
        };
        var root = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            payload, requestId, cancellationToken, preferRepresentation: true);
        return ParseRefund(root!);
    }

    public async Task<PayPalRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken)
    {
        var root = await SendJsonAsync(HttpMethod.Get,
            $"/v2/payments/refunds/{Uri.EscapeDataString(refundId)}", null, null, cancellationToken);
        return ParseRefund(root!);
    }

    public async Task<PayPalPaymentToken> CreatePaymentTokenAsync(string customerId, CardInput card,
        string requestId, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["customer"] = new JsonObject { ["merchant_customer_id"] = customerId },
            ["payment_source"] = new JsonObject { ["card"] = CardSource(card, null) }
        };
        var root = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens", payload,
            requestId, cancellationToken);
        var cardNode = root!["payment_source"]?["card"]
            ?? throw InvalidResponse("payment_source.card");
        return new PayPalPaymentToken(
            RequiredString(root, "id"),
            RequiredString(cardNode, "brand"),
            RequiredString(cardNode, "last_digits"),
            cardNode["expiry"]?.GetValue<string>());
    }

    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken)
    {
        await SendJsonAsync(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}", null, null,
            cancellationToken, allowEmpty: true);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchAllTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var transactions = new Dictionary<string, PayPalTransaction>(StringComparer.Ordinal);
        var chunkStart = from;
        while (chunkStart < to)
        {
            var chunkEnd = chunkStart.AddDays(31);
            if (chunkEnd > to) chunkEnd = to;
            await SearchChunkAsync(chunkStart, chunkEnd, transactions, cancellationToken);
            chunkStart = chunkEnd;
        }
        return transactions.Values
            .OrderBy(x => x.InitiatedAt)
            .ThenBy(x => x.TransactionId, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task SearchChunkAsync(DateTimeOffset from, DateTimeOffset to,
        IDictionary<string, PayPalTransaction> destination, CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        var page = 1;
        int totalPages;
        do
        {
            var query = $"?start_date={Uri.EscapeDataString(FormatDate(from))}" +
                        $"&end_date={Uri.EscapeDataString(FormatDate(to))}" +
                        "&fields=transaction_info&balance_affecting_records_only=N" +
                        $"&page_size={pageSize}&page={page}";
            var root = await SendJsonAsync(HttpMethod.Get, "/v1/reporting/transactions" + query,
                null, null, cancellationToken);
            if (root!["transaction_details"] is JsonArray details)
            {
                foreach (var detail in details)
                {
                    var info = detail?["transaction_info"];
                    if (info is null) continue;
                    var transaction = ParseTransaction(info);
                    var key = $"{transaction.TransactionId}|{transaction.EventCode}|{transaction.UpdatedAt:O}";
                    destination[key] = transaction;
                }
            }
            totalPages = root["total_pages"]?.GetValue<int>() ?? 1;
            page++;
        } while (page <= totalPages);
    }

    private async Task<JsonNode?> SendJsonAsync(HttpMethod method, string path, JsonNode? body,
        string? requestId, CancellationToken cancellationToken, bool allowEmpty = false,
        bool preferRepresentation = false)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = await GetAccessTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(method, BuildUri(path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (preferRepresentation)
                request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (requestId is not null)
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            if (body is not null)
                request.Content = new StringContent(body.ToJsonString(JsonOptions), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _accessToken = null;
                continue;
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw ParseError((int)response.StatusCode, text);
            if (string.IsNullOrWhiteSpace(text))
            {
                if (allowEmpty) return null;
                throw InvalidResponse("response body");
            }
            return JsonNode.Parse(text) ?? throw InvalidResponse("response body");
        }

        throw new PayPalApiException(401, "AUTHENTICATION_FAILURE",
            "PayPal rejected the OAuth access token.", null, Array.Empty<string>());
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
                throw new InvalidOperationException("PayPal credentials are not configured in the PayPal section.");

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw ParseError((int)response.StatusCode, text);
            var root = JsonNode.Parse(text) ?? throw InvalidResponse("OAuth response");
            _accessToken = RequiredString(root, "access_token");
            var expiresIn = root["expires_in"]?.GetValue<int>() ?? 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private Uri BuildUri(string path) => new(_options.ResolveBaseUrl().TrimEnd('/') + path);

    private static JsonObject CardSource(CardInput? card, string? vaultId)
    {
        if (card is null && vaultId is null)
            throw new ArgumentException("Either card details or a vault ID is required.");
        if (card is not null && vaultId is not null)
            throw new ArgumentException("Card details and a vault ID are mutually exclusive.");
        if (vaultId is not null) return new JsonObject { ["vault_id"] = vaultId };

        var address = card!.BillingAddress;
        return new JsonObject
        {
            ["name"] = card.Name,
            ["number"] = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["billing_address"] = new JsonObject
            {
                ["address_line_1"] = address.AddressLine1,
                ["address_line_2"] = address.AddressLine2,
                ["admin_area_2"] = address.City,
                ["admin_area_1"] = address.State,
                ["postal_code"] = address.PostalCode,
                ["country_code"] = address.CountryCode.ToUpperInvariant()
            }
        };
    }

    private static JsonObject Money(decimal amount, string currency) => new()
    {
        ["currency_code"] = currency.ToUpperInvariant(),
        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static PayPalAuthorization? ParseAuthorization(JsonNode root)
    {
        var paypalOrderId = RequiredString(root, "id");
        var orderStatus = RequiredString(root, "status");
        if (root["purchase_units"] is not JsonArray units) return null;
        foreach (var unit in units)
        {
            if (unit?["payments"]?["authorizations"] is not JsonArray authorizations) continue;
            var authorization = authorizations.FirstOrDefault();
            if (authorization is null) continue;
            return ParseAuthorizationNode(authorization, paypalOrderId, orderStatus);
        }
        return null;
    }

    private static PayPalAuthorization ParseStandaloneAuthorization(JsonNode root) =>
        ParseAuthorizationNode(root, string.Empty, "COMPLETED");

    private static PayPalAuthorization ParseAuthorizationNode(JsonNode node, string paypalOrderId,
        string orderStatus)
    {
        var amount = ParseMoney(node["amount"]);
        return new PayPalAuthorization(
            paypalOrderId,
            orderStatus,
            RequiredString(node, "id"),
            RequiredString(node, "status"),
            amount.Amount,
            amount.Currency,
            ParseDate(node["create_time"]) ?? DateTimeOffset.UtcNow,
            ParseDate(node["expiration_time"]),
            false);
    }

    private static PayPalRefund ParseRefund(JsonNode root)
    {
        var amount = ParseMoney(root["amount"]);
        return new PayPalRefund(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            amount.Amount,
            amount.Currency,
            ParseDate(root["create_time"]) ?? DateTimeOffset.UtcNow);
    }

    private static PayPalCapture ParseCapture(JsonNode root)
    {
        var captureAmount = ParseMoney(root["amount"]);
        var breakdown = root["seller_receivable_breakdown"];
        return new PayPalCapture(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            captureAmount.Amount,
            captureAmount.Currency,
            ParseOptionalMoney(breakdown?["paypal_fee"]),
            ParseOptionalMoney(breakdown?["net_amount"]),
            ParseDate(root["create_time"]) ?? DateTimeOffset.UtcNow);
    }

    private static PayPalTransaction ParseTransaction(JsonNode info)
    {
        var money = ParseOptionalMoneyWithCurrency(info["transaction_amount"]);
        return new PayPalTransaction(
            RequiredString(info, "transaction_id"),
            info["paypal_reference_id"]?.GetValue<string>(),
            info["paypal_reference_id_type"]?.GetValue<string>(),
            info["transaction_event_code"]?.GetValue<string>(),
            ParseDate(info["transaction_initiation_date"]),
            ParseDate(info["transaction_updated_date"]),
            money?.Amount,
            money?.Currency,
            ParseOptionalMoney(info["fee_amount"]),
            info["transaction_status"]?.GetValue<string>(),
            info["invoice_id"]?.GetValue<string>(),
            info["custom_field"]?.GetValue<string>());
    }

    private static bool RequiresPayerAction(JsonNode root) =>
        string.Equals(root["status"]?.GetValue<string>(), "PAYER_ACTION_REQUIRED",
            StringComparison.OrdinalIgnoreCase) ||
        (root["links"] as JsonArray)?.Any(link =>
            string.Equals(link?["rel"]?.GetValue<string>(), "payer-action",
                StringComparison.OrdinalIgnoreCase)) == true;

    private static (decimal Amount, string Currency) ParseMoney(JsonNode? node) =>
        ParseOptionalMoneyWithCurrency(node) ?? throw InvalidResponse("money");

    private static (decimal Amount, string Currency)? ParseOptionalMoneyWithCurrency(JsonNode? node)
    {
        if (node is null) return null;
        var value = node["value"]?.GetValue<string>();
        var currency = node["currency_code"]?.GetValue<string>();
        if (value is null || currency is null ||
            !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            return null;
        return (amount, currency);
    }

    private static decimal? ParseOptionalMoney(JsonNode? node) =>
        ParseOptionalMoneyWithCurrency(node)?.Amount;

    private static DateTimeOffset? ParseDate(JsonNode? node) =>
        DateTimeOffset.TryParse(node?.GetValue<string>(), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var date) ? date : null;

    private static string RequiredString(JsonNode node, string property) =>
        node[property]?.GetValue<string>() ?? throw InvalidResponse(property);

    private static PayPalApiException InvalidResponse(string field) => new(502,
        "INVALID_RESPONSE", $"PayPal's response omitted or invalidated '{field}'.", null,
        Array.Empty<string>());

    private static PayPalApiException ParseError(int statusCode, string responseText)
    {
        try
        {
            var root = JsonNode.Parse(responseText);
            var issues = (root?["details"] as JsonArray)?
                .Select(detail => detail?["issue"]?.GetValue<string>())
                .Where(issue => !string.IsNullOrWhiteSpace(issue))
                .Cast<string>()
                .ToArray() ?? Array.Empty<string>();
            return new PayPalApiException(
                statusCode,
                root?["name"]?.GetValue<string>() ?? "API_ERROR",
                root?["message"]?.GetValue<string>() ?? "PayPal rejected the request.",
                root?["debug_id"]?.GetValue<string>(),
                issues);
        }
        catch (JsonException)
        {
            return new PayPalApiException(statusCode, "API_ERROR",
                "PayPal returned an unreadable error response.", null, Array.Empty<string>());
        }
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

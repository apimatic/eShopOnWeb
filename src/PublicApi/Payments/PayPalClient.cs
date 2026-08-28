using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalClient
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
        _httpClient.BaseAddress = new Uri(_options.ResolveBaseUrl(), UriKind.Absolute);
    }

    internal async Task<PayPalAuthorizationResult> CreateAndAuthorizeOrderAsync(
        int orderId, string paymentReference, decimal amount, string currency, CardRequest? card, string? vaultId,
        CancellationToken cancellationToken)
    {
        var paymentCard = card is not null ? CardJson(card) : new JsonObject
        {
            ["vault_id"] = vaultId,
            ["stored_credential"] = new JsonObject
            {
                ["payment_initiator"] = "CUSTOMER",
                ["payment_type"] = "ONE_TIME",
                ["usage"] = "SUBSEQUENT"
            }
        };
        var value = Money(amount);
        var body = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["reference_id"] = $"eshop-order-{paymentReference}",
                    ["custom_id"] = paymentReference,
                    ["invoice_id"] = $"eshop-{paymentReference}",
                    ["amount"] = new JsonObject { ["currency_code"] = currency, ["value"] = value }
                }
            },
            ["payment_source"] = new JsonObject { ["card"] = paymentCard }
        };

        var created = await SendJsonAsync(HttpMethod.Post, "v2/checkout/orders", body,
            $"eshop-{paymentReference}-create", cancellationToken);
        ThrowIfPayerActionRequired(created);
        var paypalOrderId = RequiredString(created, "id");
        var orderStatus = String(created, "status") ?? "UNKNOWN";
        var authorization = FindAuthorization(created);

        if (authorization is null)
        {
            var authorized = await SendJsonAsync(HttpMethod.Post,
                $"v2/checkout/orders/{Uri.EscapeDataString(paypalOrderId)}/authorize",
                new JsonObject(), $"eshop-{paymentReference}-authorize", cancellationToken);
            ThrowIfPayerActionRequired(authorized);
            orderStatus = String(authorized, "status") ?? orderStatus;
            authorization = FindAuthorization(authorized);
        }

        if (authorization is null)
        {
            throw new PaymentApiException(StatusCodes.Status502BadGateway,
                $"PayPal order {paypalOrderId} did not contain an authorization.");
        }

        return ParseAuthorization(paypalOrderId, orderStatus, authorization);
    }

    internal async Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string paypalOrderId, string authorizationId, CancellationToken cancellationToken)
    {
        var response = await SendJsonAsync(HttpMethod.Get,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null,
            cancellationToken);
        return ParseAuthorization(paypalOrderId, "APPROVED", response);
    }

    internal async Task<PayPalAuthorizationResult> ReauthorizeAsync(string paypalOrderId,
        string authorizationId, decimal amount, string currency, string paymentReference,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["amount"] = new JsonObject { ["currency_code"] = currency, ["value"] = Money(amount) }
        };
        var response = await SendJsonAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            body, $"eshop-{paymentReference}-reauthorize", cancellationToken);
        return ParseAuthorization(paypalOrderId, "APPROVED", response);
    }

    internal async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount,
        string currency, string paymentReference, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["amount"] = new JsonObject { ["currency_code"] = currency, ["value"] = Money(amount) },
            ["invoice_id"] = $"eshop-{paymentReference}",
            ["final_capture"] = true
        };
        var response = await SendJsonAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            body, $"eshop-{paymentReference}-capture", cancellationToken);
        return new PayPalCaptureResult(
            RequiredString(response, "id"),
            RequiredString(response, "status"),
            RequiredDecimal(response, "amount", "value"),
            RequiredString(response, "amount", "currency_code"),
            Decimal(response, "seller_receivable_breakdown", "paypal_fee", "value"),
            Decimal(response, "seller_receivable_breakdown", "net_amount", "value"));
    }

    internal async Task VoidAsync(string authorizationId, string paymentReference, CancellationToken cancellationToken)
    {
        await SendJsonAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            null, $"eshop-{paymentReference}-void", cancellationToken, allowNoContent: true);
    }

    internal async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount,
        string currency, string requestId, string callerKey, string? note, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["amount"] = new JsonObject { ["currency_code"] = currency, ["value"] = Money(amount) },
            ["custom_id"] = callerKey,
            ["note_to_payer"] = note
        };
        var response = await SendJsonAsync(HttpMethod.Post,
            $"v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund", body,
            requestId, cancellationToken);
        return new PayPalRefundResult(
            RequiredString(response, "id"), RequiredString(response, "status"),
            RequiredDecimal(response, "amount", "value"),
            RequiredString(response, "amount", "currency_code"));
    }

    internal async Task<PayPalVaultResult> VaultCardAsync(string buyerId, CardRequest card,
        string requestId, CancellationToken cancellationToken)
    {
        var merchantCustomerId = MerchantCustomerId(buyerId);
        var setupBody = new JsonObject
        {
            ["customer"] = new JsonObject { ["merchant_customer_id"] = merchantCustomerId },
            ["payment_source"] = new JsonObject { ["card"] = CardJson(card) }
        };
        var setup = await SendJsonAsync(HttpMethod.Post, "v3/vault/setup-tokens", setupBody,
            requestId + "-setup", cancellationToken);
        ThrowIfPayerActionRequired(setup);
        var setupId = RequiredString(setup, "id");
        var customerId = String(setup, "customer", "id");

        var tokenBody = new JsonObject
        {
            ["customer"] = customerId is null
                ? new JsonObject { ["merchant_customer_id"] = merchantCustomerId }
                : new JsonObject { ["id"] = customerId },
            ["payment_source"] = new JsonObject
            {
                ["token"] = new JsonObject { ["id"] = setupId, ["type"] = "SETUP_TOKEN" }
            }
        };
        var token = await SendJsonAsync(HttpMethod.Post, "v3/vault/payment-tokens", tokenBody,
            requestId + "-token", cancellationToken);
        var tokenCard = Node(token, "payment_source", "card")
            ?? throw new PaymentApiException(StatusCodes.Status502BadGateway,
                "PayPal did not return safe card details for the saved payment method.");
        return new PayPalVaultResult(
            RequiredString(token, "id"), String(token, "customer", "id") ?? customerId,
            String(tokenCard, "brand") ?? "UNKNOWN",
            String(tokenCard, "last_digits") ?? throw new PaymentApiException(
                StatusCodes.Status502BadGateway, "PayPal did not return the saved card's last digits."),
            String(tokenCard, "expiry") ?? card.Expiry);
    }

    internal async Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken)
    {
        try
        {
            await SendJsonAsync(HttpMethod.Delete,
                $"v3/vault/payment-tokens/{Uri.EscapeDataString(tokenId)}", null, null,
                cancellationToken, allowNoContent: true);
        }
        catch (PaymentApiException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
        {
            // Deletion is idempotent from eShop's perspective.
        }
    }

    internal async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var transactions = new List<PayPalTransaction>();
        var page = 1;
        var totalPages = 1;
        do
        {
            var startDate = from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            var endDate = to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            var query = $"v1/reporting/transactions?start_date={Uri.EscapeDataString(startDate)}" +
                        $"&end_date={Uri.EscapeDataString(endDate)}" +
                        $"&fields=transaction_info&balance_affecting_records_only=N&page_size=500&page={page}";
            var response = await SendJsonAsync(HttpMethod.Get, query, null, null, cancellationToken);
            if (response["transaction_details"] is JsonArray details)
            {
                foreach (var detail in details.OfType<JsonObject>())
                {
                    var info = detail["transaction_info"];
                    var id = String(info, "transaction_id");
                    if (id is null) continue;
                    transactions.Add(new PayPalTransaction(
                        id,
                        String(info, "paypal_reference_id"),
                        String(info, "transaction_event_code"),
                        String(info, "transaction_status"),
                        Date(info, "transaction_initiation_date"),
                        Decimal(info, "transaction_amount", "value"),
                        Decimal(info, "fee_amount", "value"),
                        String(info, "transaction_amount", "currency_code"),
                        String(info, "invoice_id"),
                        String(info, "custom_field")));
                }
            }
            totalPages = response["total_pages"]?.GetValue<int>() ?? 1;
            page++;
        } while (page <= totalPages);
        return transactions;
    }

    private async Task<JsonObject> SendJsonAsync(HttpMethod method, string path, JsonNode? body,
        string? requestId, CancellationToken cancellationToken, bool allowNoContent = false)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            await GetAccessTokenAsync(cancellationToken));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (requestId is not null)
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(JsonOptions), Encoding.UTF8,
                "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (allowNoContent && response.StatusCode == HttpStatusCode.NoContent)
        {
            return new JsonObject();
        }
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ProcessorError(response.StatusCode, content,
                response.Headers.TryGetValues("PayPal-Debug-Id", out var values) ? values.FirstOrDefault() : null);
        }
        if (string.IsNullOrWhiteSpace(content)) return new JsonObject();
        return JsonNode.Parse(content)?.AsObject()
            ?? throw new PaymentApiException(StatusCodes.Status502BadGateway,
                "PayPal returned an invalid JSON response.");
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
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw ProcessorError(response.StatusCode, content, null);
            var json = JsonNode.Parse(content)?.AsObject()
                ?? throw new PaymentApiException(StatusCodes.Status502BadGateway,
                    "PayPal returned an invalid credential response.");
            _accessToken = RequiredString(json, "access_token");
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(json["expires_in"]?.GetValue<int>() ?? 300);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static JsonObject CardJson(CardRequest card) => new()
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
            ["country_code"] = card.BillingAddress.CountryCode
        }
    };

    private static PayPalAuthorizationResult ParseAuthorization(string paypalOrderId,
        string orderStatus, JsonNode authorization) => new(
        paypalOrderId,
        orderStatus,
        RequiredString(authorization, "id"),
        RequiredString(authorization, "status"),
        RequiredDecimal(authorization, "amount", "value"),
        RequiredString(authorization, "amount", "currency_code"),
        Date(authorization, "create_time") ?? DateTimeOffset.UtcNow,
        Date(authorization, "expiration_time"));

    private static JsonNode? FindAuthorization(JsonNode root) =>
        Node(root, "purchase_units", "0", "payments", "authorizations", "0");

    private static void ThrowIfPayerActionRequired(JsonNode response)
    {
        var payerAction = String(response, "status") == "PAYER_ACTION_REQUIRED" ||
                          response["links"] is JsonArray links && links.OfType<JsonObject>()
                              .Any(x => String(x, "rel") is "payer-action" or "approve");
        if (payerAction)
        {
            throw new PaymentApiException(StatusCodes.Status409Conflict,
                "PayPal requires an interactive cardholder challenge. This headless integration cannot continue that payment.");
        }
    }

    private static PaymentApiException ProcessorError(HttpStatusCode status, string content, string? debugId)
    {
        string message;
        try
        {
            var error = JsonNode.Parse(content);
            var name = String(error, "name") ?? "PAYPAL_ERROR";
            var detail = error?["details"] is JsonArray details
                ? details.OfType<JsonObject>().Select(x => String(x, "description"))
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                : null;
            message = $"PayPal rejected the operation ({name}): {detail ?? String(error, "message") ?? "No detail was supplied."}";
            debugId ??= String(error, "debug_id");
        }
        catch (JsonException)
        {
            message = "PayPal rejected the operation without a valid error response.";
        }
        var apiStatus = status switch
        {
            HttpStatusCode.BadRequest => StatusCodes.Status400BadRequest,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => StatusCodes.Status502BadGateway,
            HttpStatusCode.NotFound => StatusCodes.Status404NotFound,
            HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status502BadGateway
        };
        return new PaymentApiException(apiStatus, message, debugId);
    }

    private static string MerchantCustomerId(string buyerId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        return "eshop-" + Convert.ToHexString(bytes)[..32].ToLowerInvariant();
    }

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
    private static JsonNode? Node(JsonNode? node, params string[] path)
    {
        foreach (var part in path)
        {
            if (node is null) return null;
            if (int.TryParse(part, out var index))
                node = node is JsonArray array && index < array.Count ? array[index] : null;
            else
                node = node[part];
        }
        return node;
    }
    private static string? String(JsonNode? node, params string[] path) =>
        Node(node, path)?.GetValue<string>();
    private static string RequiredString(JsonNode? node, params string[] path) =>
        String(node, path) ?? throw new PaymentApiException(StatusCodes.Status502BadGateway,
            $"PayPal response omitted {string.Join('.', path)}.");
    private static decimal? Decimal(JsonNode? node, params string[] path) =>
        decimal.TryParse(String(node, path), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value : null;
    private static decimal RequiredDecimal(JsonNode? node, params string[] path) =>
        Decimal(node, path) ?? throw new PaymentApiException(StatusCodes.Status502BadGateway,
            $"PayPal response omitted {string.Join('.', path)}.");
    private static DateTimeOffset? Date(JsonNode? node, params string[] path) =>
        DateTimeOffset.TryParse(String(node, path), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var value) ? value : null;
}

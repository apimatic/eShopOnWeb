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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalClient : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalClient(IHttpClientFactory httpClientFactory, IOptions<PayPalOptions> options,
        ILogger<PayPalClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> CreateOrderAsync(int orderId, string paymentReference, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["reference_id"] = $"ESHOP-{paymentReference}",
                    ["invoice_id"] = $"ESHOP-{paymentReference}",
                    ["custom_id"] = orderId.ToString(CultureInfo.InvariantCulture),
                    ["amount"] = Money(amount, currency)
                }
            }
        };

        using var json = await SendAsync(HttpMethod.Post, "v2/checkout/orders", "create order",
            requestId, body, cancellationToken);
        return RequiredString(json.RootElement, "id", "create order");
    }

    public async Task<GatewayAuthorization> AuthorizeOrderAsync(string paypalOrderId,
        PaymentSource source, string requestId, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["payment_source"] = BuildPaymentSource(source) };
        using var json = await SendAsync(HttpMethod.Post,
            $"v2/checkout/orders/{Uri.EscapeDataString(paypalOrderId)}/authorize", "authorize order",
            requestId, body, cancellationToken);
        var root = json.RootElement;
        var orderStatus = OptionalString(root, "status") ?? "UNKNOWN";
        if (string.Equals(orderStatus, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentGatewayException("authorize order", 422, "PAYER_ACTION_REQUIRED",
                "PayPal requires browser approval for this card payment.",
                new[] { "PAYER_ACTION_REQUIRED" }, OptionalString(root, "debug_id"));
        }

        if (!root.TryGetProperty("purchase_units", out var purchaseUnits) ||
            purchaseUnits.GetArrayLength() == 0 ||
            !purchaseUnits[0].TryGetProperty("payments", out var payments) ||
            !payments.TryGetProperty("authorizations", out var authorizations) ||
            authorizations.GetArrayLength() == 0)
        {
            throw InvalidResponse("authorize order", "PayPal did not return an authorization.");
        }

        return ParseAuthorization(authorizations[0], paypalOrderId, orderStatus);
    }

    public async Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["amount"] = Money(amount, currency) };
        using var json = await SendAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            "reauthorize payment", requestId, body, cancellationToken);
        return ParseAuthorization(json.RootElement, string.Empty, "COMPLETED");
    }

    public async Task<GatewayCapture> CaptureAsync(string authorizationId, int orderId, string paymentReference,
        decimal amount, string currency, string requestId, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["amount"] = Money(amount, currency),
            ["invoice_id"] = $"ESHOP-{paymentReference}",
            ["final_capture"] = true
        };
        using var json = await SendAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            "capture payment", requestId, body, cancellationToken);
        return ParseCapture(json.RootElement, "capture payment");
    }

    public async Task<GatewayCapture> GetCaptureAsync(string captureId,
        CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Get,
            $"v2/payments/captures/{Uri.EscapeDataString(captureId)}", "get capture",
            null, null, cancellationToken);
        return ParseCapture(json.RootElement, "get capture");
    }

    public async Task VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            "void authorization", requestId, null, cancellationToken, allowEmptyResponse: true);
    }

    public async Task<GatewayRefund> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["amount"] = Money(amount, currency) };
        using var json = await SendAsync(HttpMethod.Post,
            $"v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund", "refund payment",
            requestId, body, cancellationToken);
        var root = json.RootElement;
        return new GatewayRefund(
            RequiredString(root, "id", "refund payment"),
            RequiredString(root, "status", "refund payment"),
            ReadMoney(root, "amount", "refund payment").Amount,
            ReadMoney(root, "amount", "refund payment").Currency,
            ReadDate(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task<GatewaySavedCard> SaveCardAsync(PaymentCard card, string requestId,
        CancellationToken cancellationToken)
    {
        var cardNode = BuildCard(card, includeSecurityCode: false);
        var setupBody = new JsonObject
        {
            ["payment_source"] = new JsonObject { ["card"] = cardNode }
        };
        using var setupJson = await SendAsync(HttpMethod.Post, "v3/vault/setup-tokens",
            "create card setup token", requestId + "-setup", setupBody, cancellationToken);
        var setupId = RequiredString(setupJson.RootElement, "id", "create card setup token");
        var setupStatus = RequiredString(setupJson.RootElement, "status", "create card setup token");
        if (!string.Equals(setupStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentGatewayException("create card setup token", 422, setupStatus,
                "PayPal did not approve the card for vaulting.", new[] { setupStatus }, null);
        }

        var tokenBody = new JsonObject
        {
            ["payment_source"] = new JsonObject
            {
                ["token"] = new JsonObject { ["id"] = setupId, ["type"] = "SETUP_TOKEN" }
            }
        };
        using var tokenJson = await SendAsync(HttpMethod.Post, "v3/vault/payment-tokens",
            "create payment token", requestId + "-token", tokenBody, cancellationToken);
        var root = tokenJson.RootElement;
        var cardResult = root.GetProperty("payment_source").GetProperty("card");
        return new GatewaySavedCard(
            RequiredString(root, "id", "create payment token"),
            root.TryGetProperty("customer", out var customer) ? OptionalString(customer, "id") : null,
            RequiredString(cardResult, "brand", "create payment token"),
            RequiredString(cardResult, "last_digits", "create payment token"),
            RequiredString(cardResult, "expiry", "create payment token"));
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, string requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await SendAsync(HttpMethod.Delete,
                $"v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}",
                "delete payment token", requestId, null, cancellationToken, allowEmptyResponse: true);
        }
        catch (PaymentGatewayException exception) when (exception.StatusCode == 404)
        {
            // A retry after PayPal deleted the token but before the local commit is successful in effect.
        }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<GatewayTransaction>();
        var windowStart = from.ToUniversalTime();
        var finalEnd = to.ToUniversalTime();

        while (windowStart < finalEnd)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > finalEnd) windowEnd = finalEnd;
            var page = 1;
            var totalPages = 1;
            do
            {
                var path = "v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(FormatDate(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatDate(windowEnd))}" +
                    "&fields=all&balance_affecting_records_only=N&page_size=500" +
                    $"&page={page.ToString(CultureInfo.InvariantCulture)}";
                using var json = await SendAsync(HttpMethod.Get, path, "search transactions",
                    null, null, cancellationToken);
                var root = json.RootElement;
                totalPages = root.TryGetProperty("total_pages", out var count) ? count.GetInt32() : 1;
                if (root.TryGetProperty("transaction_details", out var details))
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (!detail.TryGetProperty("transaction_info", out var info)) continue;
                        var transactionMoney = TryReadMoney(info, "transaction_amount");
                        var feeMoney = TryReadMoney(info, "fee_amount");
                        results.Add(new GatewayTransaction(
                            RequiredString(info, "transaction_id", "search transactions"),
                            OptionalString(info, "paypal_reference_id"),
                            OptionalString(info, "paypal_reference_id_type"),
                            OptionalString(info, "transaction_event_code") ?? "UNKNOWN",
                            OptionalString(info, "transaction_status") ?? "UNKNOWN",
                            ReadDate(info, "transaction_initiation_date"),
                            ReadDate(info, "transaction_updated_date"),
                            transactionMoney?.Amount,
                            transactionMoney?.Currency,
                            feeMoney?.Amount,
                            OptionalString(info, "invoice_id")));
                    }
                }

                page++;
            } while (page <= totalPages);

            windowStart = windowEnd;
        }

        return results
            .GroupBy(transaction => new
            {
                transaction.TransactionId,
                transaction.EventCode,
                transaction.InitiatedAt,
                transaction.Amount,
                transaction.Currency
            })
            .Select(group => group.First())
            .ToList();
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, string operation,
        string? requestId, JsonNode? body, CancellationToken cancellationToken,
        bool allowEmptyResponse = false)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var accessToken = await GetAccessTokenAsync(attempt > 0, cancellationToken);
            using var request = new HttpRequestMessage(method, BuildUrl(path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (!string.IsNullOrWhiteSpace(requestId))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }

            if (body is not null)
            {
                request.Content = new StringContent(body.ToJsonString(JsonOptions), Encoding.UTF8,
                    "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClientFactory.CreateClient(nameof(PayPalClient))
                    .SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException) when (attempt == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
                continue;
            }

            using (response)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    if (string.IsNullOrWhiteSpace(responseBody))
                    {
                        if (allowEmptyResponse) return JsonDocument.Parse("{}");
                        throw InvalidResponse(operation, "PayPal returned an empty response.");
                    }

                    try
                    {
                        return JsonDocument.Parse(responseBody);
                    }
                    catch (JsonException)
                    {
                        throw InvalidResponse(operation, "PayPal returned malformed JSON.");
                    }
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    InvalidateAccessToken();
                    continue;
                }

                if ((int)response.StatusCode >= 500 && attempt == 0 && !string.IsNullOrWhiteSpace(requestId))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
                    continue;
                }

                throw CreateGatewayException(operation, response.StatusCode, responseBody);
            }
        }

        throw InvalidResponse(operation, "PayPal request failed after retry.");
    }

    private async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _accessToken is not null &&
            _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _accessToken is not null &&
                _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl("v1/oauth2/token"));
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClientFactory.CreateClient(nameof(PayPalClient))
                .SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateGatewayException("get access token", response.StatusCode, responseBody);
            }

            using var json = JsonDocument.Parse(responseBody);
            _accessToken = RequiredString(json.RootElement, "access_token", "get access token");
            var expiresIn = json.RootElement.TryGetProperty("expires_in", out var expiry)
                ? expiry.GetInt32()
                : 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void InvalidateAccessToken()
    {
        _accessToken = null;
        _accessTokenExpiresAt = DateTimeOffset.MinValue;
    }

    private string BuildUrl(string path)
    {
        var configuredBase = _options.BaseUrl;
        var baseUrl = !string.IsNullOrWhiteSpace(configuredBase)
            ? configuredBase
            : string.Equals(_options.Environment, "Sandbox", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.sandbox.paypal.com"
                : "https://api-m.paypal.com";
        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    private static JsonObject BuildPaymentSource(PaymentSource source)
    {
        if (source.Card is not null)
        {
            return new JsonObject { ["card"] = BuildCard(source.Card, includeSecurityCode: true) };
        }

        if (!string.IsNullOrWhiteSpace(source.VaultId))
        {
            return new JsonObject
            {
                ["card"] = new JsonObject { ["vault_id"] = source.VaultId }
            };
        }

        throw new ArgumentException("A card or vault ID is required.", nameof(source));
    }

    private static JsonObject BuildCard(PaymentCard card, bool includeSecurityCode)
    {
        var result = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["name"] = card.Name,
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
        if (includeSecurityCode) result["security_code"] = card.SecurityCode;
        return result;
    }

    private static GatewayAuthorization ParseAuthorization(JsonElement authorization,
        string paypalOrderId, string paypalOrderStatus)
    {
        var money = ReadMoney(authorization, "amount", "authorize payment");
        return new GatewayAuthorization(
            paypalOrderId,
            paypalOrderStatus,
            RequiredString(authorization, "id", "authorize payment"),
            RequiredString(authorization, "status", "authorize payment"),
            money.Amount,
            money.Currency,
            ReadDate(authorization, "create_time") ?? DateTimeOffset.UtcNow,
            ReadDate(authorization, "expiration_time"));
    }

    private static GatewayCapture ParseCapture(JsonElement capture, string operation)
    {
        var amount = ReadMoney(capture, "amount", operation);
        decimal? fee = null;
        decimal? net = null;
        if (capture.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = TryReadMoney(breakdown, "paypal_fee")?.Amount;
            net = TryReadMoney(breakdown, "net_amount")?.Amount;
        }

        return new GatewayCapture(
            RequiredString(capture, "id", operation),
            RequiredString(capture, "status", operation),
            amount.Amount,
            amount.Currency,
            fee,
            net,
            ReadDate(capture, "create_time") ?? DateTimeOffset.UtcNow);
    }

    private PaymentGatewayException CreateGatewayException(string operation, HttpStatusCode status,
        string responseBody)
    {
        var code = "PAYPAL_ERROR";
        var issues = new List<string>();
        string? debugId = null;
        try
        {
            using var json = JsonDocument.Parse(responseBody);
            var root = json.RootElement;
            code = OptionalString(root, "name") ?? OptionalString(root, "error") ?? code;
            debugId = OptionalString(root, "debug_id");
            if (root.TryGetProperty("details", out var details))
            {
                foreach (var detail in details.EnumerateArray())
                {
                    var issue = OptionalString(detail, "issue");
                    if (!string.IsNullOrWhiteSpace(issue)) issues.Add(issue);
                }
            }
        }
        catch (JsonException)
        {
            // The raw body is intentionally neither logged nor returned.
        }

        _logger.LogWarning("PayPal {Operation} failed with HTTP {StatusCode}, code {Code}, issues {Issues}, debug id {DebugId}",
            operation, (int)status, code, string.Join(',', issues), debugId);
        var issueText = issues.Count == 0 ? code : string.Join(", ", issues);
        return new PaymentGatewayException(operation, (int)status, code,
            $"PayPal could not {operation}: {issueText}.", issues, debugId);
    }

    private static PaymentGatewayException InvalidResponse(string operation, string message) =>
        new(operation, 502, "INVALID_PAYPAL_RESPONSE", message,
            Array.Empty<string>(), null);

    private static JsonObject Money(decimal amount, string currency) => new()
    {
        ["currency_code"] = currency.ToUpperInvariant(),
        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static (decimal Amount, string Currency) ReadMoney(JsonElement parent, string property,
        string operation)
    {
        var money = parent.GetProperty(property);
        var value = RequiredString(money, "value", operation);
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            throw InvalidResponse(operation, "PayPal returned an invalid amount.");
        }

        return (amount, RequiredString(money, "currency_code", operation));
    }

    private static (decimal Amount, string Currency)? TryReadMoney(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var money)) return null;
        var value = OptionalString(money, "value");
        var currency = OptionalString(money, "currency_code");
        if (value is null || currency is null ||
            !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)) return null;
        return (amount, currency);
    }

    private static string RequiredString(JsonElement element, string property, string operation)
    {
        var result = OptionalString(element, property);
        if (string.IsNullOrWhiteSpace(result))
        {
            throw InvalidResponse(operation, $"PayPal response omitted {property}.");
        }

        return result;
    }

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? ReadDate(JsonElement element, string property)
    {
        var value = OptionalString(element, property);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var result) ? result : null;
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

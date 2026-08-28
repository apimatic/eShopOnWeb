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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalClient : IPayPalClient
{
    private const int ReportingPageSize = 500;
    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;
    private readonly string _baseUrl;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options, ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _baseUrl = !string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? _options.BaseUrl.TrimEnd('/')
            : _options.Environment.Equals("sandbox", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.sandbox.paypal.com"
                : _options.Environment.Equals("live", StringComparison.OrdinalIgnoreCase)
                    ? "https://api-m.paypal.com"
                    : throw new InvalidOperationException("PayPal:Environment must be 'sandbox' or 'live'.");
    }

    public async Task<string> CreateOrderAsync(int orderId, string paymentReference, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray(new JsonObject
            {
                ["reference_id"] = orderId.ToString(CultureInfo.InvariantCulture),
                ["custom_id"] = paymentReference,
                ["invoice_id"] = paymentReference,
                ["amount"] = Money(amount, currency)
            })
        };

        var response = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", requestId, body, cancellationToken);
        return RequiredString(response, "id");
    }

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(string paypalOrderId, CardRequest? card,
        string? vaultId, decimal expectedAmount, string requestId, CancellationToken cancellationToken)
    {
        var cardNode = card is not null ? CardNode(card) : new JsonObject
        {
            ["vault_id"] = vaultId,
            ["stored_credential"] = new JsonObject
            {
                ["payment_initiator"] = "CUSTOMER",
                ["payment_type"] = "ONE_TIME",
                ["usage"] = "SUBSEQUENT"
            }
        };
        var body = new JsonObject { ["payment_source"] = new JsonObject { ["card"] = cardNode } };
        var response = await SendAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(paypalOrderId)}/authorize", requestId, body, cancellationToken);

        ThrowIfPayerActionRequired(response);
        var authorization = response["purchase_units"]?[0]?["payments"]?["authorizations"]?[0]
            ?? throw InvalidResponse("The authorization was not present in PayPal's response.");
        var result = ParseAuthorization(paypalOrderId, authorization);
        if (result.Amount != expectedAmount)
        {
            throw InvalidResponse($"PayPal authorized {result.Amount:F2}, but the order total is {expectedAmount:F2}.");
        }
        return result;
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId,
        string paypalOrderId, decimal amount, string currency, string requestId,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["amount"] = Money(amount, currency) };
        var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            requestId, body, cancellationToken);
        return ParseAuthorization(paypalOrderId, response);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["amount"] = Money(amount, currency),
            ["final_capture"] = true
        };
        var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            requestId, body, cancellationToken);
        var capturedAmount = Decimal(response["amount"]?["value"]);
        if (capturedAmount != amount)
        {
            throw InvalidResponse($"PayPal captured {capturedAmount:F2}, but {amount:F2} was requested.");
        }
        return new PayPalCaptureResult(
            RequiredString(response, "id"), RequiredString(response, "status"), capturedAmount,
            NullableDecimal(response["seller_receivable_breakdown"]?["paypal_fee"]?["value"]),
            NullableDecimal(response["seller_receivable_breakdown"]?["net_amount"]?["value"]),
            Date(response["create_time"]) ?? DateTimeOffset.UtcNow);
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken)
    {
        await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            requestId, null, cancellationToken, allowEmpty: true);
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["amount"] = Money(amount, currency) };
        var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            requestId, body, cancellationToken);
        return new PayPalRefundResult(RequiredString(response, "id"), RequiredString(response, "status"),
            Decimal(response["amount"]?["value"]), Date(response["create_time"]) ?? DateTimeOffset.UtcNow);
    }

    public async Task<PayPalVaultResult> SaveCardAsync(string merchantCustomerId, CardRequest card,
        string requestId, CancellationToken cancellationToken)
    {
        var cardNode = CardNode(card);
        var body = new JsonObject
        {
            ["payment_source"] = new JsonObject { ["card"] = cardNode },
            ["customer"] = new JsonObject { ["merchant_customer_id"] = merchantCustomerId }
        };
        var response = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", requestId, body,
            cancellationToken);
        ThrowIfPayerActionRequired(response);
        var savedCard = response["payment_source"]?["card"]
            ?? throw InvalidResponse("The saved-card details were not present in PayPal's response.");
        var verificationStatus = savedCard["verification_status"]?.GetValue<string>();
        if (verificationStatus is not null && !verificationStatus.Equals("VERIFIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentApiException(422, "CARD_VERIFICATION_FAILED",
                $"PayPal could not verify the card (status: {verificationStatus}).");
        }
        return new PayPalVaultResult(RequiredString(response, "id"),
            response["customer"]?["id"]?.GetValue<string>(), RequiredString(savedCard, "brand"),
            RequiredString(savedCard, "last_digits"), RequiredString(savedCard, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync(HttpMethod.Delete,
                $"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}", null, null,
                cancellationToken, allowEmpty: true);
        }
        catch (PayPalException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            // Deletion is idempotent in effect. The token is already unusable at PayPal.
        }
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<PayPalTransaction>();
        var windowStart = from;
        while (windowStart <= to)
        {
            var windowEnd = windowStart.AddDays(31) < to ? windowStart.AddDays(31).AddSeconds(-1) : to;
            var page = 1;
            while (true)
            {
                var path = "/v1/reporting/transactions?" + string.Join("&", new[]
                {
                    "start_date=" + Uri.EscapeDataString(windowStart.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
                    "end_date=" + Uri.EscapeDataString(windowEnd.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
                    "fields=transaction_info",
                    "balance_affecting_records_only=N",
                    "page_size=" + ReportingPageSize.ToString(CultureInfo.InvariantCulture),
                    "page=" + page.ToString(CultureInfo.InvariantCulture)
                });
                var response = await SendAsync(HttpMethod.Get, path, null, null, cancellationToken);
                var details = response["transaction_details"]?.AsArray();
                if (details is null || details.Count == 0) break;
                foreach (var detail in details)
                {
                    var info = detail?["transaction_info"];
                    if (info is null) continue;
                    results.Add(new PayPalTransaction(
                        RequiredString(info, "transaction_id"), info["paypal_reference_id"]?.GetValue<string>(),
                        info["invoice_id"]?.GetValue<string>(), info["transaction_event_code"]?.GetValue<string>() ?? string.Empty,
                        info["transaction_status"]?.GetValue<string>() ?? string.Empty,
                        NullableDecimal(info["transaction_amount"]?["value"]),
                        info["transaction_amount"]?["currency_code"]?.GetValue<string>(),
                        NullableDecimal(info["fee_amount"]?["value"]),
                        Date(info["transaction_initiation_date"]), Date(info["transaction_updated_date"])));
                }
                if (details.Count < ReportingPageSize) break;
                page++;
            }
            if (windowEnd >= to) break;
            windowStart = windowEnd.AddSeconds(1);
        }

        return results.GroupBy(x => new { x.TransactionId, x.EventCode, x.InitiatedAt, x.Amount })
            .Select(x => x.First()).ToList();
    }

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, string? requestId,
        JsonNode? body, CancellationToken cancellationToken, bool allowEmpty = false)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, _baseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                await GetAccessTokenAsync(cancellationToken));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (!string.IsNullOrWhiteSpace(requestId))
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            if (body is not null)
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(content))
                    return allowEmpty ? new JsonObject() : throw InvalidResponse("PayPal returned an empty response.");
                try { return JsonNode.Parse(content) ?? throw InvalidResponse("PayPal returned invalid JSON."); }
                catch (JsonException ex) { throw InvalidResponse("PayPal returned invalid JSON.", ex); }
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _accessToken = null;
                continue;
            }
            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            var idempotent = method == HttpMethod.Get || !string.IsNullOrWhiteSpace(requestId);
            if (retryable && idempotent && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt) + Random.Shared.Next(100)), cancellationToken);
                continue;
            }

            throw ParseError((int)response.StatusCode, content);
        }
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
            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw ParseError((int)response.StatusCode, content);
            var json = JsonNode.Parse(content) ?? throw InvalidResponse("PayPal returned an invalid token response.");
            _accessToken = RequiredString(json, "access_token");
            var expiresIn = json["expires_in"]?.GetValue<int>() ?? 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally { _tokenLock.Release(); }
    }

    private PayPalException ParseError(int statusCode, string content)
    {
        string code = "PAYPAL_ERROR", message = "PayPal rejected the operation.";
        string? debugId = null;
        try
        {
            var json = JsonNode.Parse(content);
            code = json?["details"]?[0]?["issue"]?.GetValue<string>()
                ?? json?["name"]?.GetValue<string>() ?? code;
            message = json?["details"]?[0]?["description"]?.GetValue<string>()
                ?? json?["message"]?.GetValue<string>() ?? message;
            var field = json?["details"]?[0]?["field"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(field)) message += $" Field: {field}.";
            debugId = json?["debug_id"]?.GetValue<string>();
        }
        catch (JsonException) { }
        _logger.LogWarning("PayPal request failed with HTTP {StatusCode}, code {Code}, debug ID {DebugId}",
            statusCode, code, debugId);
        return new PayPalException(statusCode, code, message, debugId);
    }

    private static PayPalAuthorizationResult ParseAuthorization(string paypalOrderId, JsonNode node) =>
        new(paypalOrderId, RequiredString(node, "id"), RequiredString(node, "status"),
            Decimal(node["amount"]?["value"]), Date(node["create_time"]) ?? DateTimeOffset.UtcNow,
            Date(node["expiration_time"]));

    private static JsonObject CardNode(CardRequest card) => new()
    {
        ["name"] = card.Name,
        ["number"] = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal),
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

    private static JsonObject Money(decimal amount, string currency) => new()
    {
        ["currency_code"] = currency.ToUpperInvariant(),
        ["value"] = amount.ToString("F2", CultureInfo.InvariantCulture)
    };

    private static void ThrowIfPayerActionRequired(JsonNode response)
    {
        var status = response["status"]?.GetValue<string>();
        var hasPayerAction = response["links"]?.AsArray().Any(x =>
            string.Equals(x?["rel"]?.GetValue<string>(), "payer-action", StringComparison.OrdinalIgnoreCase)) == true;
        if (hasPayerAction || string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            throw new PaymentApiException(409, "PAYER_ACTION_REQUIRED",
                "PayPal requires a browser challenge for this card; this API intentionally does not implement an approval round-trip.");
    }

    private static string RequiredString(JsonNode node, string property) =>
        node[property]?.GetValue<string>() is { Length: > 0 } value
            ? value : throw InvalidResponse($"PayPal's response omitted '{property}'.");

    private static decimal Decimal(JsonNode? node) =>
        decimal.TryParse(node?.GetValue<string>(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value : throw InvalidResponse("PayPal returned an invalid amount.");

    private static decimal? NullableDecimal(JsonNode? node) => node is null ? null : Decimal(node);
    private static DateTimeOffset? Date(JsonNode? node) => node is null ? null :
        DateTimeOffset.TryParse(node.GetValue<string>(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var value) ? value : null;

    private static PaymentApiException InvalidResponse(string message, Exception? inner = null) =>
        new(502, "INVALID_PAYPAL_RESPONSE", message, innerException: inner);
}

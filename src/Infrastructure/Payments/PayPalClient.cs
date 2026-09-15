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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalClient : IPayPalClient
{
    private const string ReturnRepresentation = "return=representation";
    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options,
        ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(45);
    }

    public async Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency,
        string invoiceId, string customId, string requestId, CancellationToken cancellationToken)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = customId,
                    custom_id = customId,
                    invoice_id = invoiceId,
                    amount = Money(amount, currency)
                }
            }
        };
        var json = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId,
            cancellationToken);
        return new PayPalOrderResult(Required(json, "id"), Required(json, "status"));
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string orderId,
        PayPalCard? card, string? vaultId, string requestId, CancellationToken cancellationToken)
    {
        object cardSource;
        if (card is not null)
        {
            cardSource = CardBody(card);
        }
        else if (!string.IsNullOrWhiteSpace(vaultId))
        {
            cardSource = new
            {
                vault_id = vaultId,
                stored_credential = new
                {
                    payment_initiator = "CUSTOMER",
                    payment_type = "ONE_TIME",
                    usage = "SUBSEQUENT"
                }
            };
        }
        else
        {
            throw new ArgumentException("A card or vault ID is required.");
        }

        var body = new { payment_source = new { card = cardSource } };
        var json = await SendAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}/authorize", body, requestId,
            cancellationToken);

        var status = Required(json, "status");
        var needsAction = status == "PAYER_ACTION_REQUIRED" ||
            json["links"]?.AsArray().Any(x => x?["rel"]?.GetValue<string>() is "payer-action" or "approve") == true;
        if (needsAction)
        {
            return new PayPalAuthorizationResult(string.Empty, status, 0, string.Empty,
                null, null, true, status);
        }

        var authorization = json["purchase_units"]?[0]?["payments"]?["authorizations"]?[0]
            ?? throw InvalidResponse("PayPal did not return an authorization.");
        return ParseAuthorization(authorization, false, status);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId,
        decimal amount, string currency, string requestId, CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = Money(amount, currency) }, requestId, cancellationToken);
        return ParseAuthorization(json, false, null);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new { amount = Money(amount, currency), final_capture = true }, requestId,
            cancellationToken);
        return ParseCapture(json);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId,
        CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null,
            cancellationToken);
        return ParseCapture(json);
    }

    public async Task<string> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            null, requestId, cancellationToken);
        return json.Count == 0 ? "VOIDED" : Required(json, "status");
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount,
        string currency, string requestId, string customId, string? note,
        CancellationToken cancellationToken)
    {
        object body = amount.HasValue
            ? new { amount = Money(amount.Value, currency), custom_id = customId, note_to_payer = note }
            : new { };
        var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund", body,
            requestId, cancellationToken);
        return ParseRefund(json);
    }

    public async Task<PayPalRefundResult> GetRefundAsync(string refundId,
        CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Get,
            $"/v2/payments/refunds/{Uri.EscapeDataString(refundId)}", null, null,
            cancellationToken);
        return ParseRefund(json);
    }

    public async Task<PayPalSavedCardResult> SaveCardAsync(PayPalCard card, string requestId,
        CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens",
            new { payment_source = new { card = CardBody(card) } }, requestId,
            cancellationToken);
        var savedCard = json["payment_source"]?["card"]
            ?? throw InvalidResponse("PayPal did not return saved-card details.");
        return new PayPalSavedCardResult(Required(json, "id"), Required(savedCard, "brand"),
            Required(savedCard, "last_digits"), Required(savedCard, "expiry"),
            Optional(savedCard, "name"));
    }

    public async Task DeleteSavedCardAsync(string vaultId, CancellationToken cancellationToken)
    {
        await SendAsync(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}", null, null,
            cancellationToken, allowNotFound: true);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var transactions = new Dictionary<string, PayPalTransaction>(StringComparer.Ordinal);
        var windowStart = from.ToUniversalTime();
        var finalEnd = to.ToUniversalTime();

        while (windowStart < finalEnd)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > finalEnd)
            {
                windowEnd = finalEnd;
            }

            var page = 1;
            var totalPages = 1;
            do
            {
                var path = "/v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(Rfc3339(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(Rfc3339(windowEnd))}" +
                    "&fields=transaction_info&balance_affecting_records_only=N&page_size=500" +
                    $"&page={page}";
                JsonObject json;
                try
                {
                    json = await SendAsync(HttpMethod.Get, path, null, null, cancellationToken);
                }
                catch (PayPalApiException ex) when (
                    ex.StatusCode == HttpStatusCode.NotFound && ex.ErrorName == "INVALID_REQUEST")
                {
                    // Sandbox reporting can have no available snapshot yet for a fresh range.
                    // The live payment responses remain the source of truth for checkout.
                    break;
                }
                totalPages = json["total_pages"]?.GetValue<int>() ?? 1;
                foreach (var detail in json["transaction_details"]?.AsArray() ?? new JsonArray())
                {
                    var info = detail?["transaction_info"];
                    if (info is null)
                    {
                        continue;
                    }

                    var transaction = ParseTransaction(info);
                    var key = $"{transaction.TransactionId}|{transaction.EventCode}|{transaction.UpdatedAt:O}";
                    transactions[key] = transaction;
                }

                page++;
            } while (page <= totalPages);

            windowStart = windowEnd;
        }

        return transactions.Values.OrderBy(x => x.InitiatedAt).ToList();
    }

    private async Task<JsonObject> SendAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var token = await GetAccessTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(method, BuildUri(path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", ReturnRepresentation);
            if (!string.IsNullOrWhiteSpace(requestId))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }

            if (body is not null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8,
                    "application/json");
            }

            using var response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseBody = response.Content is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode || allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return string.IsNullOrWhiteSpace(responseBody)
                    ? new JsonObject()
                    : JsonNode.Parse(responseBody)?.AsObject() ?? new JsonObject();
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _accessToken = null;
                continue;
            }

            if ((response.StatusCode == HttpStatusCode.TooManyRequests ||
                 (int)response.StatusCode >= 500) && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken);
                continue;
            }

            throw ParseError(response.StatusCode, responseBody);
        }

        throw new InvalidOperationException("The PayPal request retry loop exited unexpectedly.");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return _accessToken;
            }

            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials"
                });

                using var response = await _httpClient.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = JsonNode.Parse(responseBody)?.AsObject()
                        ?? throw InvalidResponse("PayPal returned an empty OAuth response.");
                    _accessToken = Required(json, "access_token");
                    var expiresIn = json["expires_in"]?.GetValue<int>() ?? 300;
                    _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
                    return _accessToken;
                }

                if ((response.StatusCode == HttpStatusCode.TooManyRequests ||
                     (int)response.StatusCode >= 500) && attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken);
                    continue;
                }

                throw ParseError(response.StatusCode, responseBody);
            }
        }
        finally
        {
            _tokenLock.Release();
        }

        throw new InvalidOperationException("The PayPal OAuth retry loop exited unexpectedly.");
    }

    private Uri BuildUri(string path)
    {
        var configured = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? _options.Environment.Equals("Live", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.paypal.com"
                : "https://api-m.sandbox.paypal.com"
            : _options.BaseUrl;
        return new Uri($"{configured!.TrimEnd('/')}{path}", UriKind.Absolute);
    }

    private PayPalApiException ParseError(HttpStatusCode statusCode, string responseBody)
    {
        string name = "PAYPAL_API_ERROR";
        string message = "The payment processor rejected the request.";
        string? debugId = null;
        var issues = new List<string>();
        try
        {
            var json = JsonNode.Parse(responseBody);
            name = Optional(json, "name") ?? name;
            message = Optional(json, "message") ?? message;
            debugId = Optional(json, "debug_id");
            foreach (var detail in json?["details"]?.AsArray() ?? new JsonArray())
            {
                var issue = Optional(detail, "issue");
                if (!string.IsNullOrWhiteSpace(issue))
                {
                    issues.Add(issue);
                }
            }
        }
        catch (JsonException)
        {
            // Deliberately do not retain or log raw response bodies.
        }

        _logger.LogWarning("PayPal request failed with HTTP {Status}, error {Error}, debug ID {DebugId}, issues {Issues}",
            (int)statusCode, name, debugId, string.Join(',', issues));
        return new PayPalApiException(statusCode, name, message, debugId, issues);
    }

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
            country_code = card.BillingAddress.CountryCode
        }
    };

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency.ToUpperInvariant(),
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static PayPalAuthorizationResult ParseAuthorization(JsonNode json, bool requiresAction,
        string? paypalOrderStatus)
    {
        var money = json["amount"] ?? throw InvalidResponse("Authorization amount was missing.");
        return new PayPalAuthorizationResult(Required(json, "id"), Required(json, "status"),
            Decimal(money, "value"), Required(money, "currency_code"),
            Date(json, "create_time") ?? Date(json, "update_time"),
            Date(json, "expiration_time"), requiresAction, paypalOrderStatus);
    }

    private static PayPalCaptureResult ParseCapture(JsonNode json)
    {
        var money = json["amount"] ?? throw InvalidResponse("Capture amount was missing.");
        var breakdown = json["seller_receivable_breakdown"];
        return new PayPalCaptureResult(Required(json, "id"), Required(json, "status"),
            Decimal(money, "value"), Required(money, "currency_code"),
            NullableDecimal(breakdown?["paypal_fee"], "value"),
            NullableDecimal(breakdown?["net_amount"], "value"), Date(json, "create_time"));
    }

    private static PayPalRefundResult ParseRefund(JsonNode json)
    {
        var money = json["amount"] ?? throw InvalidResponse("Refund amount was missing.");
        return new PayPalRefundResult(Required(json, "id"), Required(json, "status"),
            Decimal(money, "value"), Required(money, "currency_code"),
            Date(json, "create_time"), Date(json, "update_time"));
    }

    private static PayPalTransaction ParseTransaction(JsonNode info)
    {
        var amount = info["transaction_amount"];
        var fee = info["fee_amount"];
        return new PayPalTransaction(Required(info, "transaction_id"),
            Optional(info, "paypal_reference_id"), Optional(info, "paypal_reference_id_type"),
            Optional(info, "transaction_event_code"), Date(info, "transaction_initiation_date"),
            Date(info, "transaction_updated_date"), NullableDecimal(amount, "value"),
            Optional(amount, "currency_code"), NullableDecimal(fee, "value"),
            Optional(info, "transaction_status"), Optional(info, "invoice_id"));
    }

    private static string Required(JsonNode? node, string property) =>
        Optional(node, property) ?? throw InvalidResponse($"PayPal response field '{property}' was missing.");

    private static string? Optional(JsonNode? node, string property) =>
        node?[property]?.GetValue<string>();

    private static decimal Decimal(JsonNode? node, string property) =>
        decimal.Parse(Required(node, property), NumberStyles.Number, CultureInfo.InvariantCulture);

    private static decimal? NullableDecimal(JsonNode? node, string property) =>
        decimal.TryParse(Optional(node, property), NumberStyles.Number, CultureInfo.InvariantCulture,
            out var value) ? value : null;

    private static DateTimeOffset? Date(JsonNode? node, string property) =>
        DateTimeOffset.TryParse(Optional(node, property), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var value) ? value : null;

    private static string Rfc3339(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static InvalidOperationException InvalidResponse(string message) =>
        new($"Invalid PayPal response: {message}");
}

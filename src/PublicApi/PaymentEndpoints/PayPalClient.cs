using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PayPalClient : IPayPalClient
{
    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options, ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeCommand command,
        CancellationToken cancellationToken)
    {
        var amount = Money(command.Amount);
        var purchaseUnit = new JsonObject
        {
            ["reference_id"] = "default",
            ["custom_id"] = command.CorrelationId,
            ["invoice_id"] = command.InvoiceId,
            ["amount"] = MoneyObject(command.Currency, amount)
        };
        var orderRequest = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray(purchaseUnit)
        };

        var created = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", orderRequest,
            RequestId("order-create", command.CorrelationId), cancellationToken);
        var orderId = RequiredString(created, "id");

        JsonObject card;
        if (command.Card is not null)
        {
            card = CardObject(command.Card);
        }
        else if (!string.IsNullOrWhiteSpace(command.VaultId))
        {
            card = new JsonObject
            {
                ["vault_id"] = command.VaultId,
                ["stored_credential"] = new JsonObject
                {
                    ["payment_initiator"] = "CUSTOMER",
                    ["payment_type"] = "UNSCHEDULED",
                    ["usage"] = "SUBSEQUENT"
                }
            };
        }
        else
        {
            throw new ArgumentException("A card or vault ID is required.");
        }

        var authorizeRequest = new JsonObject
        {
            ["payment_source"] = new JsonObject { ["card"] = card }
        };
        var authorized = await SendAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}/authorize", authorizeRequest,
            RequestId("order-authorize", command.CorrelationId), cancellationToken);
        ThrowIfPayerActionRequired(authorized);

        var authorization = authorized?["purchase_units"]?[0]?["payments"]?["authorizations"]?[0]
            ?? throw new PaymentOperationException(HttpStatusCode.BadGateway,
                "PayPal did not return an authorization for the order.");
        return ParseAuthorization(authorization, orderId);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null,
            cancellationToken);
        return ParseAuthorization(response, string.Empty);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string idempotencySeed, CancellationToken cancellationToken)
    {
        var request = new JsonObject { ["amount"] = MoneyObject(currency, Money(amount)) };
        var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize", request,
            RequestId("reauthorize", idempotencySeed), cancellationToken);
        return ParseAuthorization(response, string.Empty);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount,
        string currency, string invoiceId, string idempotencySeed, CancellationToken cancellationToken)
    {
        var request = new JsonObject
        {
            ["amount"] = MoneyObject(currency, Money(amount)),
            ["invoice_id"] = invoiceId,
            ["final_capture"] = true
        };
        var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture", request,
            RequestId("capture", idempotencySeed), cancellationToken);
        return ParseCapture(response);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null, cancellationToken);
        return ParseCapture(response);
    }

    public async Task VoidAsync(string authorizationId, string idempotencySeed,
        CancellationToken cancellationToken)
    {
        await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void", new JsonObject(),
            RequestId("void", idempotencySeed), cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string? note, string idempotencySeed, CancellationToken cancellationToken)
    {
        var request = new JsonObject { ["amount"] = MoneyObject(currency, Money(amount)) };
        if (!string.IsNullOrWhiteSpace(note)) request["note_to_payer"] = note;
        var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund", request,
            RequestId("refund", idempotencySeed), cancellationToken);
        return new PayPalRefundResult(
            RequiredString(response, "id"),
            RequiredString(response, "status"),
            RequiredDecimal(response?["amount"], "value"),
            RequiredString(response?["amount"], "currency_code"),
            ParseDate(response, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task<PayPalSavedCardResult> SaveCardAsync(PayPalCard card, string merchantCustomerId,
        string idempotencySeed, CancellationToken cancellationToken)
    {
        var paymentTokenRequest = new JsonObject
        {
            ["customer"] = new JsonObject { ["merchant_customer_id"] = merchantCustomerId },
            ["payment_source"] = new JsonObject { ["card"] = CardObject(card) }
        };
        var token = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", paymentTokenRequest,
            RequestId("vault-token", idempotencySeed), cancellationToken);
        ThrowIfPayerActionRequired(token);
        var tokenCard = token?["payment_source"]?["card"]
            ?? throw new PaymentOperationException(HttpStatusCode.BadGateway,
                "PayPal did not return safe card metadata for the saved payment method.");
        return new PayPalSavedCardResult(
            RequiredString(token, "id"),
            String(token?["customer"], "id"),
            RequiredString(tokenCard, "brand"),
            RequiredString(tokenCard, "last_digits"),
            RequiredString(tokenCard, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        await SendAsync(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}", null, null,
            cancellationToken, allowNotFound: true);
    }

    public async Task<IReadOnlyList<PayPalTransactionResult>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<PayPalTransactionResult>();
        var cursor = from.ToUniversalTime();
        // PayPal documents a reporting delay of up to three hours and rejects windows that
        // extend into data that is not yet reportable.
        var reportableEnd = DateTimeOffset.UtcNow.AddHours(-3);
        var end = to.ToUniversalTime() < reportableEnd ? to.ToUniversalTime() : reportableEnd;
        if (cursor > end) return results;
        while (cursor <= end)
        {
            var chunkEnd = cursor.AddDays(30) < end ? cursor.AddDays(30) : end;
            var page = 1;
            while (true)
            {
                var path = "/v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(cursor.ToString("O", CultureInfo.InvariantCulture))}" +
                    $"&end_date={Uri.EscapeDataString(chunkEnd.ToString("O", CultureInfo.InvariantCulture))}" +
                    "&fields=transaction_info&balance_affecting_records_only=N&page_size=500" +
                    $"&page={page}";
                var response = await SendAsync(HttpMethod.Get, path, null, null, cancellationToken);
                var details = response?["transaction_details"]?.AsArray();
                if (details is not null)
                {
                    foreach (var detail in details)
                    {
                        var info = detail?["transaction_info"];
                        if (info is null) continue;
                        var amountNode = info["transaction_amount"];
                        var feeNode = info["fee_amount"];
                        results.Add(new PayPalTransactionResult(
                            RequiredString(info, "transaction_id"),
                            String(info, "paypal_reference_id"),
                            String(info, "invoice_id"),
                            String(info, "custom_field"),
                            String(info, "transaction_event_code"),
                            String(info, "transaction_status"),
                            Decimal(amountNode, "value"),
                            Decimal(feeNode, "value"),
                            String(amountNode, "currency_code"),
                            ParseDate(info, "transaction_initiation_date")));
                    }
                }
                var totalPages = Integer(response, "total_pages") ?? 1;
                if (page >= totalPages || details is null || details.Count == 0) break;
                page++;
            }
            if (chunkEnd == end) break;
            cursor = chunkEnd.AddTicks(1);
        }

        return results
            .GroupBy(x => $"{x.TransactionId}|{x.EventCode}|{x.InitiationDate:O}|{x.Amount}")
            .Select(x => x.First())
            .ToList();
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body,
        string? requestId, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var token = await GetAccessTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(method, BuildUri(path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (requestId is not null) request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            if (body is not null)
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                InvalidateToken();
                continue;
            }
            if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds((200 * (1 << attempt)) + Random.Shared.Next(25, 150)),
                    cancellationToken);
                continue;
            }
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return null;
            if (response.StatusCode == HttpStatusCode.NoContent) return null;

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) ThrowPayPalError(response.StatusCode, content);
            return string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);
        }
        throw new PaymentOperationException(HttpStatusCode.BadGateway, "PayPal did not accept the request after safe retries.");
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
            ValidateOptions();
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) ThrowPayPalError(response.StatusCode, content);
            var json = JsonNode.Parse(content);
            _accessToken = RequiredString(json, "access_token");
            var expiresIn = Integer(json, "expires_in") ?? 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void InvalidateToken()
    {
        _accessToken = null;
        _accessTokenExpiresAt = DateTimeOffset.MinValue;
    }

    private Uri BuildUri(string path)
    {
        ValidateOptions();
        var configured = _options.BaseUrl;
        var baseUrl = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : _options.Environment.Equals("Sandbox", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.sandbox.paypal.com"
                : _options.Environment.Equals("Live", StringComparison.OrdinalIgnoreCase) ||
                  _options.Environment.Equals("Production", StringComparison.OrdinalIgnoreCase)
                    ? "https://api-m.paypal.com"
                    : throw new InvalidOperationException("PayPal:Environment must be Sandbox or Live unless PayPal:BaseUrl is set.");
        return new Uri($"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}", UriKind.Absolute);
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new InvalidOperationException("PayPal credentials are not configured.");
        if (string.IsNullOrWhiteSpace(_options.BaseUrl) && string.IsNullOrWhiteSpace(_options.Environment))
            throw new InvalidOperationException("PayPal:Environment is not configured.");
    }

    private void ThrowPayPalError(HttpStatusCode statusCode, string content)
    {
        string? name = null;
        string? debugId = null;
        var issues = new List<string>();
        try
        {
            var error = JsonNode.Parse(content);
            name = String(error, "name");
            debugId = String(error, "debug_id");
            if (error?["details"] is JsonArray details)
            {
                issues.AddRange(details.Select(x => String(x, "issue")).Where(x => x is not null)!);
            }
        }
        catch (JsonException) { }

        _logger.LogWarning("PayPal request failed with status {Status}, error {Error}, issues {Issues}, debug ID {DebugId}",
            (int)statusCode, name, string.Join(',', issues), debugId);
        var issueText = issues.Count == 0 ? (name ?? "API_ERROR") : string.Join(", ", issues);
        var debugText = string.IsNullOrWhiteSpace(debugId) ? string.Empty : $" PayPal debug ID: {debugId}.";
        var clientStatus = (int)statusCode >= 500 ? HttpStatusCode.BadGateway : statusCode;
        throw new PayPalApiException(clientStatus, $"PayPal rejected the operation ({issueText}).{debugText}",
            debugId, issues.ToArray());
    }

    private static void ThrowIfPayerActionRequired(JsonNode? response)
    {
        var status = String(response, "status");
        var hasPayerAction = response?["links"] is JsonArray links &&
            links.Any(x => String(x, "rel") is "payer-action" or "approve");
        if (status == "PAYER_ACTION_REQUIRED" || hasPayerAction)
            throw new PayPalPayerActionRequiredException(String(response, "debug_id") ?? "not supplied");
    }

    private static PayPalAuthorizationResult ParseAuthorization(JsonNode? node, string orderId)
    {
        return new PayPalAuthorizationResult(
            orderId,
            RequiredString(node, "id"),
            RequiredString(node, "status"),
            RequiredDecimal(node?["amount"], "value"),
            RequiredString(node?["amount"], "currency_code"),
            ParseDate(node, "create_time") ?? DateTimeOffset.UtcNow,
            ParseDate(node, "update_time"),
            ParseDate(node, "expiration_time"));
    }

    private static PayPalCaptureResult ParseCapture(JsonNode? node)
    {
        var breakdown = node?["seller_receivable_breakdown"];
        return new PayPalCaptureResult(
            RequiredString(node, "id"),
            RequiredString(node, "status"),
            RequiredDecimal(node?["amount"], "value"),
            RequiredString(node?["amount"], "currency_code"),
            Decimal(breakdown?["paypal_fee"], "value"),
            Decimal(breakdown?["net_amount"], "value"),
            ParseDate(node, "create_time"));
    }

    private static JsonObject CardObject(PayPalCard card) => new()
    {
        ["name"] = card.Name,
        ["number"] = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
        ["expiry"] = card.Expiry,
        ["security_code"] = card.SecurityCode,
        ["billing_address"] = new JsonObject
        {
            ["country_code"] = card.CountryCode,
            ["address_line_1"] = card.AddressLine1,
            ["address_line_2"] = card.AddressLine2,
            ["admin_area_1"] = card.AdminArea1,
            ["admin_area_2"] = card.AdminArea2,
            ["postal_code"] = card.PostalCode
        }
    };

    private static JsonObject MoneyObject(string currency, string amount) => new()
    {
        ["currency_code"] = currency.ToUpperInvariant(),
        ["value"] = amount
    };

    private static string Money(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero)
        .ToString("0.00", CultureInfo.InvariantCulture);

    private static string RequestId(string operation, string seed)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
        return $"eshop-{operation}-{hash}";
    }

    private static string RequiredString(JsonNode? node, string name) => String(node, name)
        ?? throw new PaymentOperationException(HttpStatusCode.BadGateway, $"PayPal response omitted {name}.");

    private static string? String(JsonNode? node, string name) => node?[name]?.GetValue<string>();

    private static decimal RequiredDecimal(JsonNode? node, string name) => Decimal(node, name)
        ?? throw new PaymentOperationException(HttpStatusCode.BadGateway, $"PayPal response omitted {name}.");

    private static decimal? Decimal(JsonNode? node, string name)
    {
        var value = String(node, name);
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static int? Integer(JsonNode? node, string name)
    {
        try
        {
            return node?[name]?.GetValue<int>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static DateTimeOffset? ParseDate(JsonNode? node, string name)
    {
        var value = String(node, name);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}

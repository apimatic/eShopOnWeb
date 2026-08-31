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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options, TimeProvider timeProvider)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<PayPalOrderCreationResult> CreateOrderAsync(int orderId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = $"eshop-order-{orderId}",
                    custom_id = orderId.ToString(CultureInfo.InvariantCulture),
                    invoice_id = requestId,
                    amount = Money(amount, currency)
                }
            }
        };
        var json = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId, cancellationToken);
        return new PayPalOrderCreationResult(RequiredString(json, "id"), RequiredString(json, "status"));
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string paypalOrderId,
        PayPalPaymentSource paymentSource, string requestId, CancellationToken cancellationToken)
    {
        var card = paymentSource.Card is not null
            ? CardPayload(paymentSource.Card)
            : new Dictionary<string, object?> { ["vault_id"] = paymentSource.VaultId };
        var body = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = card }
        };
        var json = await SendAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(paypalOrderId)}/authorize", body, requestId,
            cancellationToken);
        ThrowIfPayerActionRequired(json, "card authorization");
        return ParseOrderAuthorization(json);
    }

    public async Task<PayPalAuthorizationResult?> GetOrderAuthorizationAsync(string paypalOrderId,
        CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Get,
            $"/v2/checkout/orders/{Uri.EscapeDataString(paypalOrderId)}", null, null, cancellationToken);
        var authorizations = json["purchase_units"]?[0]?["payments"]?["authorizations"]?.AsArray();
        return authorizations is null || authorizations.Count == 0 ? null : ParseOrderAuthorization(json);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null,
            cancellationToken);
        return ParseAuthorization(json);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = Money(amount, currency) }, requestId, cancellationToken);
        return ParseAuthorization(json);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new { amount = Money(amount, currency), final_capture = true }, requestId, cancellationToken,
            preferRepresentation: true);
        return ParseCapture(json);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null, cancellationToken);
        return ParseCapture(json);
    }

    public async Task<PayPalVoidResult> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void", new { }, requestId,
            cancellationToken, allowEmptySuccess: true);
        return new PayPalVoidResult(
            json["id"]?.GetValue<string>() ?? authorizationId,
            json["status"]?.GetValue<string>() ?? "VOIDED");
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new { amount = Money(amount, currency) }, requestId, cancellationToken, preferRepresentation: true);
        return ParseRefund(json);
    }

    public async Task<PayPalRefundResult> GetRefundAsync(string refundId, CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Get,
            $"/v2/payments/refunds/{Uri.EscapeDataString(refundId)}", null, null, cancellationToken);
        return ParseRefund(json);
    }

    public async Task<PayPalSavedCardResult> SaveCardAsync(PayPalCardDetails card, string? customerId,
        string setupRequestId, string tokenRequestId, CancellationToken cancellationToken)
    {
        var setupBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = CardPayload(card) }
        };
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            setupBody["customer"] = new { id = customerId };
        }

        var setup = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody, setupRequestId,
            cancellationToken);
        ThrowIfPayerActionRequired(setup, "saving this card");
        var setupStatus = RequiredString(setup, "status");
        if (!string.Equals(setupStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalApiException(422, "SETUP_TOKEN_NOT_APPROVED",
                $"PayPal returned setup-token status {setupStatus}.", null, Array.Empty<string>());
        }

        var setupTokenId = RequiredString(setup, "id");
        var token = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", new
        {
            payment_source = new
            {
                token = new { id = setupTokenId, type = "SETUP_TOKEN" }
            }
        }, tokenRequestId, cancellationToken);

        var cardNode = RequiredObject(RequiredObject(token, "payment_source"), "card");
        var customer = RequiredObject(token, "customer");
        return new PayPalSavedCardResult(
            RequiredString(token, "id"),
            RequiredString(customer, "id"),
            RequiredString(cardNode, "brand"),
            RequiredString(cardNode, "last_digits"),
            RequiredString(cardNode, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        await SendAsync(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}", null, null, cancellationToken,
            allowEmptySuccess: true);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to <= from) throw new ArgumentException("The reconciliation end must be after its start.");

        var transactions = new Dictionary<string, PayPalTransaction>(StringComparer.Ordinal);
        var cursor = from;
        while (cursor < to)
        {
            var chunkEnd = cursor.AddDays(31) < to ? cursor.AddDays(31) : to;
            var page = 1;
            while (true)
            {
                var query = $"?start_date={Uri.EscapeDataString(Iso(cursor))}" +
                            $"&end_date={Uri.EscapeDataString(Iso(chunkEnd))}" +
                            "&fields=transaction_info&balance_affecting_records_only=N&page_size=500" +
                            $"&page={page.ToString(CultureInfo.InvariantCulture)}";
                var json = await SendAsync(HttpMethod.Get, "/v1/reporting/transactions" + query, null, null,
                    cancellationToken);
                var details = json["transaction_details"]?.AsArray() ?? new JsonArray();
                foreach (var detail in details)
                {
                    var info = detail?["transaction_info"]?.AsObject();
                    if (info is null) continue;
                    var parsed = ParseTransaction(info);
                    var key = string.Join('|', parsed.TransactionId, parsed.EventCode,
                        parsed.UpdatedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                        parsed.Amount.ToString(CultureInfo.InvariantCulture));
                    transactions[key] = parsed;
                }

                var totalPages = json["total_pages"]?.GetValue<int?>();
                if (totalPages.HasValue ? page >= totalPages.Value : details.Count < 500) break;
                page++;
            }

            cursor = chunkEnd;
        }

        return transactions.Values.OrderBy(x => x.InitiatedAt).ToArray();
    }

    private async Task<JsonObject> SendAsync(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken, bool preferRepresentation = false, bool allowEmptySuccess = false)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var accessToken = await GetAccessTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(method, BuildUri(path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(requestId)) request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            if (preferRepresentation) request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (path.StartsWith("/v1/reporting/", StringComparison.Ordinal))
                request.Headers.TryAddWithoutValidation("PayPal-Enforce-ISO8601-Format", "true");
            if (body is not null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8,
                    "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), _timeProvider, cancellationToken);
                continue;
            }

            using (response)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    if (string.IsNullOrWhiteSpace(content))
                        return allowEmptySuccess ? new JsonObject() : throw new JsonException("PayPal returned an empty response.");
                    return JsonNode.Parse(content)?.AsObject() ?? throw new JsonException("PayPal returned invalid JSON.");
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt < 3)
                {
                    _accessToken = null;
                    continue;
                }
                if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), _timeProvider, cancellationToken);
                    continue;
                }

                throw CreateApiException(response.StatusCode, content);
            }
        }

        throw new InvalidOperationException("PayPal request retry loop exited unexpectedly.");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (_accessToken is not null && now < _accessTokenExpiresAt) return _accessToken;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            now = _timeProvider.GetUtcNow();
            if (_accessToken is not null && now < _accessTokenExpiresAt) return _accessToken;
            ValidateOptions();

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw CreateApiException(response.StatusCode, content);
            var json = JsonNode.Parse(content)?.AsObject() ?? throw new JsonException("PayPal returned invalid token JSON.");
            _accessToken = RequiredString(json, "access_token");
            var expiresIn = json["expires_in"]?.GetValue<int>() ?? 300;
            _accessTokenExpiresAt = now.AddSeconds(Math.Max(30, expiresIn - 60));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new InvalidOperationException("PayPal credentials are not configured.");
        if (string.IsNullOrWhiteSpace(_options.Currency) || _options.Currency.Length != 3)
            throw new InvalidOperationException("PayPal:Currency must be a three-letter ISO-4217 currency code.");
        _ = BuildUri("/");
    }

    private Uri BuildUri(string path)
    {
        var configured = _options.BaseUrl;
        string baseUrl;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            baseUrl = configured;
        }
        else if (string.Equals(_options.Environment, "sandbox", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = "https://api-m.sandbox.paypal.com";
        }
        else if (string.Equals(_options.Environment, "live", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(_options.Environment, "production", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = "https://api-m.paypal.com";
        }
        else
        {
            throw new InvalidOperationException("PayPal:Environment must be Sandbox or Live when PayPal:BaseUrl is not set.");
        }

        return new Uri(baseUrl.TrimEnd('/') + "/" + path.TrimStart('/'), UriKind.Absolute);
    }

    private static Dictionary<string, object?> CardPayload(PayPalCardDetails card) => new()
    {
        ["number"] = card.Number,
        ["expiry"] = card.Expiry,
        ["security_code"] = card.SecurityCode,
        ["name"] = card.Name,
        ["billing_address"] = new Dictionary<string, object?>
        {
            ["address_line_1"] = card.BillingAddress.AddressLine1,
            ["address_line_2"] = card.BillingAddress.AddressLine2,
            ["admin_area_2"] = card.BillingAddress.AdminArea2,
            ["admin_area_1"] = card.BillingAddress.AdminArea1,
            ["postal_code"] = card.BillingAddress.PostalCode,
            ["country_code"] = card.BillingAddress.CountryCode
        }
    };

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency,
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static PayPalAuthorizationResult ParseOrderAuthorization(JsonObject order)
    {
        var authorizations = order["purchase_units"]?[0]?["payments"]?["authorizations"]?.AsArray()
            ?? throw new JsonException("PayPal order response contained no authorizations.");
        var authorization = authorizations.LastOrDefault()?.AsObject()
            ?? throw new JsonException("PayPal order response contained no authorization.");
        var parsed = ParseAuthorization(authorization);
        var card = order["payment_source"]?["card"]?.AsObject();
        return parsed with
        {
            PayPalOrderId = RequiredString(order, "id"),
            PayPalOrderStatus = RequiredString(order, "status"),
            CardBrand = card?["brand"]?.GetValue<string>(),
            CardLast4 = card?["last_digits"]?.GetValue<string>()
        };
    }

    private static PayPalAuthorizationResult ParseAuthorization(JsonObject authorization)
    {
        var money = RequiredObject(authorization, "amount");
        return new PayPalAuthorizationResult(
            string.Empty,
            RequiredString(authorization, "status"),
            RequiredString(authorization, "id"),
            RequiredString(authorization, "status"),
            ParseDecimal(RequiredString(money, "value")),
            RequiredString(money, "currency_code"),
            OptionalDate(authorization, "create_time") ?? DateTimeOffset.MinValue,
            OptionalDate(authorization, "expiration_time"),
            null,
            null);
    }

    private static PayPalCaptureResult ParseCapture(JsonObject capture)
    {
        var amount = RequiredObject(capture, "amount");
        var breakdown = capture["seller_receivable_breakdown"]?.AsObject();
        return new PayPalCaptureResult(
            RequiredString(capture, "id"),
            RequiredString(capture, "status"),
            ParseDecimal(RequiredString(amount, "value")),
            RequiredString(amount, "currency_code"),
            OptionalMoney(breakdown, "paypal_fee"),
            OptionalMoney(breakdown, "net_amount"),
            OptionalDate(capture, "create_time"));
    }

    private static PayPalRefundResult ParseRefund(JsonObject refund)
    {
        var money = RequiredObject(refund, "amount");
        return new PayPalRefundResult(
            RequiredString(refund, "id"),
            RequiredString(refund, "status"),
            ParseDecimal(RequiredString(money, "value")),
            RequiredString(money, "currency_code"),
            OptionalDate(refund, "create_time"));
    }

    private static PayPalTransaction ParseTransaction(JsonObject info)
    {
        var amount = RequiredObject(info, "transaction_amount");
        return new PayPalTransaction(
            RequiredString(info, "transaction_id"),
            info["paypal_reference_id"]?.GetValue<string>(),
            info["paypal_reference_id_type"]?.GetValue<string>(),
            RequiredString(info, "transaction_event_code"),
            RequiredString(info, "transaction_status"),
            OptionalDate(info, "transaction_initiation_date") ?? DateTimeOffset.MinValue,
            OptionalDate(info, "transaction_updated_date"),
            ParseDecimal(RequiredString(amount, "value")),
            OptionalMoney(info, "fee_amount"),
            RequiredString(amount, "currency_code"));
    }

    private static void ThrowIfPayerActionRequired(JsonObject json, string operation)
    {
        if (string.Equals(json["status"]?.GetValue<string>(), "PAYER_ACTION_REQUIRED",
                StringComparison.OrdinalIgnoreCase))
            throw new PayPalPayerActionRequiredException(operation);

        var links = json["links"]?.AsArray();
        if (links?.Any(link =>
                string.Equals(link?["rel"]?.GetValue<string>(), "payer-action", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(link?["rel"]?.GetValue<string>(), "approve", StringComparison.OrdinalIgnoreCase)) == true)
            throw new PayPalPayerActionRequiredException(operation);
    }

    private static PayPalApiException CreateApiException(HttpStatusCode statusCode, string content)
    {
        try
        {
            var json = JsonNode.Parse(content)?.AsObject();
            var issues = json?["details"]?.AsArray()
                .Select(x => x?["issue"]?.GetValue<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToArray() ?? Array.Empty<string>();
            return new PayPalApiException((int)statusCode,
                json?["name"]?.GetValue<string>() ?? "PAYPAL_ERROR",
                json?["message"]?.GetValue<string>() ?? "PayPal rejected the operation.",
                json?["debug_id"]?.GetValue<string>(), issues);
        }
        catch (JsonException)
        {
            return new PayPalApiException((int)statusCode, "PAYPAL_ERROR",
                "PayPal rejected the operation.", null, Array.Empty<string>());
        }
    }

    private static string RequiredString(JsonObject json, string property) =>
        json[property]?.GetValue<string>() ?? throw new JsonException($"PayPal response omitted {property}.");

    private static JsonObject RequiredObject(JsonObject json, string property) =>
        json[property]?.AsObject() ?? throw new JsonException($"PayPal response omitted {property}.");

    private static decimal ParseDecimal(string value) =>
        decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static decimal? OptionalMoney(JsonObject? json, string property)
    {
        var value = json?[property]?["value"]?.GetValue<string>();
        return value is null ? null : ParseDecimal(value);
    }

    private static DateTimeOffset? OptionalDate(JsonObject json, string property)
    {
        var value = json[property]?.GetValue<string>();
        return value is null ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static string Iso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}

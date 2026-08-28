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
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal REST client implemented against the OpenAPI documents under api-specs/paypal:
/// checkout_orders_v2, payments_payment_v2, vault_payment_tokens_v3 and transaction_search_v1.
/// No PayPal SDK is used.
/// </summary>
public sealed class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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

    public async Task<PayPalAuthorization> AuthorizeAsync(decimal amount, string currency,
        Guid paymentReference, PayPalPaymentSource source, CancellationToken cancellationToken)
    {
        var formattedAmount = FormatAmount(amount);
        var invoiceId = InvoiceId(paymentReference);
        var createBody = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = $"order-{paymentReference:N}",
                    custom_id = $"eshop:{paymentReference:N}",
                    invoice_id = invoiceId,
                    amount = new { currency_code = currency, value = formattedAmount }
                }
            }
        };

        using var orderDocument = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders",
            createBody, $"order-{paymentReference:N}", cancellationToken);
        ThrowIfPayerActionRequired(orderDocument.RootElement);
        var payPalOrderId = RequiredString(orderDocument.RootElement, "id");

        var authorizeBody = new { payment_source = BuildPaymentSource(source) };
        using var authorizationDocument = await SendJsonAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize",
            authorizeBody, $"authorize-{paymentReference:N}", cancellationToken);
        ThrowIfPayerActionRequired(authorizationDocument.RootElement);

        return ParseAuthorization(authorizationDocument.RootElement, payPalOrderId);
    }

    public async Task<PayPalOrderState> GetOrderStateAsync(string payPalOrderId,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get,
            $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}", null, null, cancellationToken);
        var root = document.RootElement;
        PayPalAuthorization? authorization = null;
        PayPalCapture? capture = null;

        if (TryFirstPayment(root, "authorizations", out var authorizationElement))
        {
            authorization = ParseAuthorizationElement(authorizationElement, payPalOrderId,
                OptionalString(root, "status") ?? "UNKNOWN");
        }

        if (TryFirstPayment(root, "captures", out var captureElement))
        {
            capture = ParseCapture(captureElement);
        }

        return new PayPalOrderState(authorization, capture);
    }

    public async Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, Guid paymentReference, CancellationToken cancellationToken)
    {
        var body = new { amount = new { currency_code = currency, value = FormatAmount(amount) } };
        var requestId = $"reauthorize-{ShortHash($"{paymentReference:N}:{authorizationId}")}";
        try
        {
            using var document = await SendJsonAsync(HttpMethod.Post,
                $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
                body, requestId, cancellationToken);
            return ParseAuthorizationElement(document.RootElement, string.Empty, "APPROVED");
        }
        catch (PayPalApiException ex) when ((int)ex.StatusCode is 400 or 404 or 422)
        {
            throw new PayPalApiException(ex.StatusCode, ex.ErrorName,
                "The authorization is outside PayPal's renewable window. Ask the shopper to authorize the order again before fulfilment",
                ex.Issue, ex.DebugId);
        }
    }

    public async Task<PayPalCapture> CaptureAsync(string authorizationId, decimal amount,
        string currency, Guid paymentReference, CancellationToken cancellationToken)
    {
        var body = new
        {
            amount = new { currency_code = currency, value = FormatAmount(amount) },
            final_capture = true
        };
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            body, $"capture-{paymentReference:N}", cancellationToken);
        return ParseCapture(document.RootElement);
    }

    public async Task<string> VoidAsync(string authorizationId, Guid paymentReference,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            null, $"void-{paymentReference:N}", cancellationToken);
        return document.RootElement.ValueKind == JsonValueKind.Object
            ? OptionalString(document.RootElement, "status") ?? "VOIDED"
            : "VOIDED";
    }

    public async Task<PayPalRefund> RefundAsync(string captureId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var body = new { amount = new { currency_code = currency, value = FormatAmount(amount) } };
        var requestId = $"refund-{ShortHash($"{captureId}:{idempotencyKey}")}";
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            body, requestId, cancellationToken);
        var root = document.RootElement;
        return new PayPalRefund(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            MoneyValue(root, "amount") ?? amount,
            OptionalDate(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task<PayPalRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get,
            $"/v2/payments/refunds/{Uri.EscapeDataString(refundId)}", null, null, cancellationToken);
        var root = document.RootElement;
        return new PayPalRefund(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            MoneyValue(root, "amount") ?? throw InvalidResponse("refund amount"),
            OptionalDate(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task<PayPalVaultedCard> SaveCardAsync(string buyerId, PaymentCard card,
        CancellationToken cancellationToken)
    {
        var operationId = Guid.NewGuid();
        var body = new
        {
            customer = new { merchant_customer_id = $"eshop-{ShortHash(buyerId)}" },
            payment_source = new { card = BuildCard(card) }
        };
        using var document = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens",
            body, $"vault-{operationId:N}", cancellationToken);
        ThrowIfPayerActionRequired(document.RootElement);

        var root = document.RootElement;
        if (!root.TryGetProperty("payment_source", out var paymentSource) ||
            !paymentSource.TryGetProperty("card", out var cardResponse))
        {
            throw new PayPalApiException(HttpStatusCode.BadGateway, "INVALID_RESPONSE",
                "PayPal did not return the saved card description", null, null);
        }

        string? customerId = null;
        if (root.TryGetProperty("customer", out var customer))
        {
            customerId = OptionalString(customer, "id");
        }

        return new PayPalVaultedCard(
            RequiredString(root, "id"),
            customerId,
            OptionalString(cardResponse, "brand") ?? "UNKNOWN",
            RequiredString(cardResponse, "last_digits"),
            OptionalString(cardResponse, "expiry") ?? card.Expiry);
    }

    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await SendAsync(HttpMethod.Delete,
                $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}", null, null, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // The desired state is already true; local deletion may safely continue.
        }
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from >= to)
        {
            throw new ArgumentException("The reconciliation 'from' value must be earlier than 'to'.");
        }

        var results = new Dictionary<string, PayPalTransaction>(StringComparer.Ordinal);
        var rangeStart = from.ToUniversalTime();
        var requestedEnd = to.ToUniversalTime();

        while (rangeStart < requestedEnd)
        {
            // The Transaction Search contract limits one request to 31 days.
            var rangeEnd = rangeStart.AddDays(30) < requestedEnd ? rangeStart.AddDays(30) : requestedEnd;
            var page = 1;
            while (true)
            {
                var query = $"?start_date={EncodeDate(rangeStart)}&end_date={EncodeDate(rangeEnd)}" +
                            $"&fields=transaction_info&page_size=500&page={page}";
                using var document = await SendAsync(HttpMethod.Get,
                    "/v1/reporting/transactions" + query, null, null, cancellationToken);
                var root = document.RootElement;
                var count = 0;
                if (root.TryGetProperty("transaction_details", out var details) &&
                    details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (!detail.TryGetProperty("transaction_info", out var info)) continue;
                        var transaction = ParseTransaction(info);
                        results[$"{transaction.TransactionId}:{transaction.EventCode}"] = transaction;
                        count++;
                    }
                }

                var totalPages = OptionalInt(root, "total_pages");
                if ((totalPages.HasValue && page >= totalPages.Value) ||
                    (!totalPages.HasValue && count < 500))
                {
                    break;
                }
                page++;
            }

            if (rangeEnd == requestedEnd) break;
            rangeStart = rangeEnd;
        }

        return results.Values.OrderBy(t => t.InitiatedAt).ToList();
    }

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, object body,
        string? requestId, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        return await SendAsync(method, path, new StringContent(json, Encoding.UTF8, "application/json"),
            requestId, cancellationToken);
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, HttpContent? content,
        string? requestId, CancellationToken cancellationToken)
    {
        _options.EnsureValid();
        using var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            await GetAccessTokenAsync(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        request.Content = content;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ParseError(response.StatusCode, payload);
        }

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "null" : payload);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw ParseError(response.StatusCode, payload);
            }

            using var document = JsonDocument.Parse(payload);
            _accessToken = RequiredString(document.RootElement, "access_token");
            var expiresIn = OptionalInt(document.RootElement, "expires_in") ?? 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 30));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private Uri BuildUri(string path) => new(_options.ResolveBaseUrl().TrimEnd('/') + path);

    private static object BuildPaymentSource(PayPalPaymentSource source) => source switch
    {
        PayPalPaymentSource.OneOffCard oneOff => new { card = BuildCard(oneOff.Card) },
        PayPalPaymentSource.VaultedCard vaulted => new
        {
            card = new
            {
                vault_id = vaulted.VaultId,
                stored_credential = new
                {
                    payment_initiator = "CUSTOMER",
                    payment_type = "ONE_TIME",
                    usage = "SUBSEQUENT"
                }
            }
        },
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    private static object BuildCard(PaymentCard card) => new
    {
        name = card.Name,
        number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
        expiry = card.Expiry,
        security_code = card.SecurityCode,
        billing_address = card.BillingAddress is null ? null : new
        {
            address_line_1 = card.BillingAddress.AddressLine1,
            address_line_2 = card.BillingAddress.AddressLine2,
            admin_area_2 = card.BillingAddress.City,
            admin_area_1 = card.BillingAddress.State,
            postal_code = card.BillingAddress.PostalCode,
            country_code = card.BillingAddress.CountryCode
        }
    };

    private static PayPalAuthorization ParseAuthorization(JsonElement root, string payPalOrderId)
    {
        if (!TryFirstPayment(root, "authorizations", out var authorization))
        {
            throw new PayPalApiException(HttpStatusCode.BadGateway, "INVALID_RESPONSE",
                "PayPal did not return an authorization", null, null);
        }
        return ParseAuthorizationElement(authorization, payPalOrderId,
            OptionalString(root, "status") ?? "UNKNOWN");
    }

    private static PayPalAuthorization ParseAuthorizationElement(JsonElement element,
        string payPalOrderId, string orderStatus) => new(
            payPalOrderId,
            orderStatus,
            RequiredString(element, "id"),
            RequiredString(element, "status"),
            MoneyValue(element, "amount") ?? throw InvalidResponse("authorization amount"),
            OptionalDate(element, "create_time") ?? DateTimeOffset.UtcNow,
            OptionalDate(element, "expiration_time"));

    private static PayPalCapture ParseCapture(JsonElement element)
    {
        decimal? fee = null;
        decimal? net = null;
        if (element.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = MoneyValue(breakdown, "paypal_fee");
            net = MoneyValue(breakdown, "net_amount");
        }

        return new PayPalCapture(
            RequiredString(element, "id"),
            RequiredString(element, "status"),
            MoneyValue(element, "amount") ?? throw InvalidResponse("capture amount"),
            fee,
            net,
            OptionalDate(element, "create_time") ?? DateTimeOffset.UtcNow);
    }

    private static PayPalTransaction ParseTransaction(JsonElement info) => new(
        RequiredString(info, "transaction_id"),
        OptionalString(info, "paypal_reference_id"),
        OptionalString(info, "paypal_reference_id_type"),
        OptionalString(info, "invoice_id"),
        OptionalString(info, "custom_field"),
        OptionalString(info, "transaction_event_code"),
        OptionalString(info, "transaction_status"),
        MoneyValue(info, "transaction_amount"),
        MoneyValue(info, "fee_amount"),
        MoneyCurrency(info, "transaction_amount"),
        OptionalDate(info, "transaction_initiation_date"),
        OptionalDate(info, "transaction_updated_date"));

    private static bool TryFirstPayment(JsonElement root, string collectionName, out JsonElement payment)
    {
        payment = default;
        var found = false;
        var latest = DateTimeOffset.MinValue;
        if (!root.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var unit in units.EnumerateArray())
        {
            if (!unit.TryGetProperty("payments", out var payments) ||
                !payments.TryGetProperty(collectionName, out var collection) ||
                collection.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var candidate in collection.EnumerateArray())
            {
                var createdAt = OptionalDate(candidate, "create_time") ?? DateTimeOffset.MinValue;
                if (!found || createdAt >= latest)
                {
                    payment = candidate;
                    latest = createdAt;
                    found = true;
                }
            }
        }
        return found;
    }

    private static void ThrowIfPayerActionRequired(JsonElement root)
    {
        if (OptionalString(root, "status") == "PAYER_ACTION_REQUIRED")
        {
            throw new PayPalPayerActionRequiredException(
                "PayPal requires an interactive payer challenge; this direct-card API does not implement a browser approval round-trip.");
        }
    }

    private static PayPalApiException ParseError(HttpStatusCode statusCode, string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var name = OptionalString(root, "name") ?? "PAYPAL_ERROR";
            var message = OptionalString(root, "message") ?? "The processor rejected the request";
            var debugId = OptionalString(root, "debug_id");
            string? issue = null;
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    issue = OptionalString(detail, "issue");
                    message = OptionalString(detail, "description") ?? message;
                    break;
                }
            }
            return new PayPalApiException(statusCode, name, message, issue, debugId);
        }
        catch (JsonException)
        {
            return new PayPalApiException(statusCode, "PAYPAL_ERROR",
                "The processor returned an unreadable error response", null, null);
        }
    }

    private static Exception InvalidResponse(string field) => new PayPalApiException(
        HttpStatusCode.BadGateway, "INVALID_RESPONSE", $"PayPal omitted the {field}", null, null);

    private static string RequiredString(JsonElement element, string propertyName) =>
        OptionalString(element, propertyName) ?? throw InvalidResponse(propertyName);

    private static string? OptionalString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? OptionalInt(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) &&
        value.TryGetInt32(out var number) ? number : null;

    private static DateTimeOffset? OptionalDate(JsonElement element, string propertyName) =>
        DateTimeOffset.TryParse(OptionalString(element, propertyName), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var value) ? value : null;

    private static decimal? MoneyValue(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var money)) return null;
        return decimal.TryParse(OptionalString(money, "value"), NumberStyles.Number,
            CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string? MoneyCurrency(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var money) ? OptionalString(money, "currency_code") : null;

    private static string FormatAmount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string InvoiceId(Guid paymentReference) => $"ESHOP-{paymentReference:N}";
    private static string ShortHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..40];
    private static string EncodeDate(DateTimeOffset value) =>
        Uri.EscapeDataString(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
}

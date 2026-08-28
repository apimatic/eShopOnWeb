using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalClient : IPayPalClient
{
    public const string HttpClientName = "PayPal";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalClient(IHttpClientFactory httpClientFactory, IOptions<PayPalOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<PayPalAuthorization> AuthorizeOrderAsync(
        int orderId,
        string integrationId,
        string invoiceId,
        decimal amount,
        string currency,
        PayPalCardDetails? card,
        string? vaultId,
        CancellationToken cancellationToken)
    {
        if ((card == null) == string.IsNullOrWhiteSpace(vaultId))
        {
            throw new ArgumentException("Supply exactly one card source.");
        }

        var cardPayload = card == null
            ? new Dictionary<string, object?> { ["vault_id"] = vaultId }
            : CardPayload(card);

        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = $"eshop-{integrationId}",
                    custom_id = orderId.ToString(CultureInfo.InvariantCulture),
                    invoice_id = invoiceId,
                    amount = Money(amount, currency)
                }
            },
            payment_source = new { card = cardPayload }
        };

        using var created = await SendJsonAsync(
            HttpMethod.Post,
            "v2/checkout/orders",
            body,
            $"eshop-{integrationId}-create",
            cancellationToken);

        ThrowIfPayerActionRequired(created.RootElement);
        var payPalOrderId = RequiredString(created.RootElement, "id");
        var authorization = TryReadAuthorization(created.RootElement, payPalOrderId);
        if (authorization != null)
        {
            return authorization;
        }

        using var authorized = await SendJsonAsync(
            HttpMethod.Post,
            $"v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize",
            new { },
            $"eshop-{integrationId}-authorize",
            cancellationToken);

        ThrowIfPayerActionRequired(authorized.RootElement);
        return TryReadAuthorization(authorized.RootElement, payPalOrderId)
            ?? throw new PayPalApiException(
                HttpStatusCode.BadGateway,
                "INVALID_PAYPAL_RESPONSE",
                null,
                null,
                "PayPal completed the order without returning an authorization ID.");
    }

    public async Task<PayPalAuthorization> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(
            HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = Money(amount, currency) },
            requestId,
            cancellationToken);

        var root = response.RootElement;
        return new PayPalAuthorization(
            string.Empty,
            "COMPLETED",
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            ReadMoney(root, "amount"),
            OptionalDate(root, "create_time"),
            OptionalDate(root, "expiration_time"),
            null,
            null);
    }

    public async Task<PayPalCapture> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(
            HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new { amount = Money(amount, currency), final_capture = true },
            requestId,
            cancellationToken,
            preferRepresentation: true);

        var root = response.RootElement;
        decimal? fee = null;
        decimal? net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = TryReadMoney(breakdown, "paypal_fee");
            net = TryReadMoney(breakdown, "net_amount");
        }

        return new PayPalCapture(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            ReadMoney(root, "amount"),
            fee,
            net,
            OptionalDate(root, "create_time"));
    }

    public async Task<PayPalCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(
            HttpMethod.Get,
            $"v2/payments/captures/{Uri.EscapeDataString(captureId)}",
            null,
            null,
            cancellationToken);
        var root = response.RootElement;
        decimal? fee = null;
        decimal? net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = TryReadMoney(breakdown, "paypal_fee");
            net = TryReadMoney(breakdown, "net_amount");
        }

        return new PayPalCapture(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            ReadMoney(root, "amount"),
            fee,
            net,
            OptionalDate(root, "create_time"));
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            content: null,
            requestId,
            cancellationToken,
            preferRepresentation: false);
    }

    public async Task<PayPalRefund> RefundAsync(
        string captureId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(
            HttpMethod.Post,
            $"v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new { amount = Money(amount, currency) },
            requestId,
            cancellationToken,
            preferRepresentation: true);

        var root = response.RootElement;
        return new PayPalRefund(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            ReadMoney(root, "amount"));
    }

    public async Task<PayPalSavedCard> SaveCardAsync(
        string merchantCustomerId,
        string? payPalCustomerId,
        PayPalCardDetails card,
        CancellationToken cancellationToken)
    {
        var customer = string.IsNullOrWhiteSpace(payPalCustomerId)
            ? new Dictionary<string, object?> { ["merchant_customer_id"] = merchantCustomerId }
            : new Dictionary<string, object?> { ["id"] = payPalCustomerId };

        using var setup = await SendJsonAsync(
            HttpMethod.Post,
            "v3/vault/setup-tokens",
            new { customer, payment_source = new { card = CardPayload(card) } },
            $"eshop-vault-setup-{Guid.NewGuid():N}",
            cancellationToken);

        ThrowIfPayerActionRequired(setup.RootElement);
        var setupTokenId = RequiredString(setup.RootElement, "id");

        using var token = await SendJsonAsync(
            HttpMethod.Post,
            "v3/vault/payment-tokens",
            new
            {
                payment_source = new
                {
                    token = new { id = setupTokenId, type = "SETUP_TOKEN" }
                }
            },
            $"eshop-vault-token-{setupTokenId}",
            cancellationToken);

        var root = token.RootElement;
        var tokenId = RequiredString(root, "id");
        var customerId = RequiredNestedString(root, "customer", "id");
        var cardResult = RequiredObject(RequiredObject(root, "payment_source"), "card");
        return new PayPalSavedCard(
            tokenId,
            customerId,
            RequiredString(cardResult, "brand"),
            RequiredString(cardResult, "last_digits"),
            RequiredString(cardResult, "expiry"),
            OptionalString(cardResult, "name"));
    }

    public async Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Delete,
            $"v3/vault/payment-tokens/{Uri.EscapeDataString(tokenId)}",
            content: null,
            requestId: null,
            cancellationToken,
            preferRepresentation: false);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> GetTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to)
        {
            throw new ArgumentException("The reconciliation start must be before its end.");
        }

        var transactions = new List<PayPalTransaction>();
        var windowStart = from.ToUniversalTime();
        var requestedEnd = to.ToUniversalTime();

        while (windowStart < requestedEnd)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > requestedEnd) windowEnd = requestedEnd;

            var page = 1;
            while (true)
            {
                var query = $"v1/reporting/transactions?start_date={Uri.EscapeDataString(Iso(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(Iso(windowEnd))}&fields=transaction_info" +
                    $"&balance_affecting_records_only=N&page_size=500&page={page}";
                using var document = await SendJsonAsync(HttpMethod.Get, query, null, null, cancellationToken);
                var root = document.RootElement;
                if (root.TryGetProperty("transaction_details", out var details))
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        var info = RequiredObject(detail, "transaction_info");
                        var amountElement = RequiredObject(info, "transaction_amount");
                        transactions.Add(new PayPalTransaction(
                            RequiredString(info, "transaction_id"),
                            OptionalString(info, "paypal_reference_id"),
                            OptionalString(info, "paypal_reference_id_type"),
                            OptionalString(info, "invoice_id"),
                            OptionalString(info, "transaction_event_code") ?? string.Empty,
                            OptionalString(info, "transaction_status") ?? string.Empty,
                            OptionalDate(info, "transaction_initiation_date"),
                            OptionalDate(info, "transaction_updated_date"),
                            ParseDecimal(RequiredString(amountElement, "value")),
                            RequiredString(amountElement, "currency_code"),
                            TryReadMoney(info, "fee_amount")));
                    }
                }

                var totalPages = root.TryGetProperty("total_pages", out var totalPagesElement)
                    ? totalPagesElement.GetInt32()
                    : page;
                if (page >= totalPages) break;
                page++;
            }

            windowStart = windowEnd;
        }

        return transactions
            .GroupBy(x => new { x.Id, x.EventCode, x.Status, x.Amount, x.InitiatedAt })
            .Select(x => x.First())
            .ToList();
    }

    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool preferRepresentation = false)
    {
        HttpContent? content = body == null ? null : JsonContent.Create(body, options: JsonOptions);
        using var response = await SendAsync(method, path, content, requestId, cancellationToken, preferRepresentation);
        if (response.Content.Headers.ContentLength == 0)
        {
            return JsonDocument.Parse("{}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        string? requestId,
        CancellationToken cancellationToken,
        bool preferRepresentation)
    {
        _options.Validate();
        var token = await GetAccessTokenAsync(cancellationToken);
        var request = new HttpRequestMessage(method, new Uri(_options.GetBaseUri(), path.TrimStart('/')))
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(requestId)) request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        if (preferRepresentation) request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (path.StartsWith("v1/reporting/", StringComparison.Ordinal))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Enforce-ISO8601-Format", "true");
        }

        var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        request.Dispose();
        if (response.IsSuccessStatusCode) return response;

        await ThrowPayPalErrorAsync(response, cancellationToken);
        throw new InvalidOperationException("Unreachable");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken != null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken != null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;

            _options.Validate();
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.GetBaseUri(), "v1/oauth2/token"));
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });

            using var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) await ThrowPayPalErrorAsync(response, cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            _accessToken = RequiredString(document.RootElement, "access_token");
            var seconds = document.RootElement.TryGetProperty("expires_in", out var expiry) ? expiry.GetInt32() : 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static async Task ThrowPayPalErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string name = "PAYPAL_API_ERROR";
        string? issue = null;
        string? debugId = null;
        string message = $"PayPal returned HTTP {(int)response.StatusCode}.";
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            name = OptionalString(root, "name") ?? name;
            debugId = OptionalString(root, "debug_id");
            message = OptionalString(root, "message") ?? message;
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                var first = details.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object)
                {
                    issue = OptionalString(first, "issue");
                    message = OptionalString(first, "description") ?? message;
                }
            }
        }
        catch (JsonException)
        {
            // Deliberately do not include raw PayPal response bodies in exceptions or logs.
        }

        response.Dispose();
        throw new PayPalApiException(response.StatusCode, name, issue, debugId, message);
    }

    private static Dictionary<string, object?> CardPayload(PayPalCardDetails card) => new()
    {
        ["number"] = card.Number,
        ["expiry"] = card.Expiry,
        ["security_code"] = card.SecurityCode,
        ["name"] = card.Name,
        ["billing_address"] = new
        {
            address_line_1 = card.BillingAddress.AddressLine1,
            address_line_2 = card.BillingAddress.AddressLine2,
            admin_area_2 = card.BillingAddress.City,
            admin_area_1 = card.BillingAddress.State,
            postal_code = card.BillingAddress.PostalCode,
            country_code = card.BillingAddress.CountryCode.ToUpperInvariant()
        }
    };

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency.ToUpperInvariant(),
        value = amount.ToString("F2", CultureInfo.InvariantCulture)
    };

    private static PayPalAuthorization? TryReadAuthorization(JsonElement root, string payPalOrderId)
    {
        if (!root.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array) return null;
        foreach (var unit in units.EnumerateArray())
        {
            if (!unit.TryGetProperty("payments", out var payments) ||
                !payments.TryGetProperty("authorizations", out var authorizations) ||
                authorizations.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var authorization = authorizations.EnumerateArray().FirstOrDefault();
            if (authorization.ValueKind != JsonValueKind.Object) continue;
            string? brand = null;
            string? lastDigits = null;
            if (root.TryGetProperty("payment_source", out var source) &&
                source.TryGetProperty("card", out var card))
            {
                brand = OptionalString(card, "brand");
                lastDigits = OptionalString(card, "last_digits");
            }

            return new PayPalAuthorization(
                payPalOrderId,
                OptionalString(root, "status") ?? "COMPLETED",
                RequiredString(authorization, "id"),
                RequiredString(authorization, "status"),
                ReadMoney(authorization, "amount"),
                OptionalDate(authorization, "create_time"),
                OptionalDate(authorization, "expiration_time"),
                brand,
                lastDigits);
        }

        return null;
    }

    private static void ThrowIfPayerActionRequired(JsonElement root)
    {
        var status = OptionalString(root, "status");
        if (!string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)) return;
        throw new PayPalApiException(
            HttpStatusCode.UnprocessableEntity,
            "PAYER_ACTION_REQUIRED",
            "PAYER_ACTION_REQUIRED",
            null,
            "PayPal requires a browser cardholder challenge; this headless API flow cannot continue.");
    }

    private static JsonElement RequiredObject(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw new JsonException($"PayPal response omitted {property}.");

    private static string RequiredNestedString(JsonElement parent, string objectProperty, string stringProperty) =>
        RequiredString(RequiredObject(parent, objectProperty), stringProperty);

    private static string RequiredString(JsonElement parent, string property) =>
        OptionalString(parent, property) ?? throw new JsonException($"PayPal response omitted {property}.");

    private static string? OptionalString(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? OptionalDate(JsonElement parent, string property) =>
        DateTimeOffset.TryParse(OptionalString(parent, property), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;

    private static decimal ReadMoney(JsonElement parent, string property) =>
        ParseDecimal(RequiredString(RequiredObject(parent, property), "value"));

    private static decimal? TryReadMoney(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? ParseDecimal(RequiredString(value, "value"))
            : null;

    private static decimal ParseDecimal(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
    private static string Iso(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

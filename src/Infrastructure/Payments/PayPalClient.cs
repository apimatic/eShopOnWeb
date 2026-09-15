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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly string _baseUrl;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _baseUrl = ResolveBaseUrl(_options);
        Currency = Require(_options.Currency, "PayPal:Currency").ToUpperInvariant();
    }

    public string Currency { get; }

    public async Task<PayPalOrderResult> CreateOrderAsync(string paymentReference, decimal amount, string requestId,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = $"eshop-order-{paymentReference}",
                    custom_id = paymentReference,
                    invoice_id = $"ESHOP-{paymentReference}",
                    amount = Money(amount)
                }
            }
        };
        using var json = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId, cancellationToken);
        return new PayPalOrderResult(RequiredString(json.RootElement, "id"), RequiredString(json.RootElement, "status"));
    }

    public Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string paypalOrderId, PayPalCard card,
        string requestId, CancellationToken cancellationToken) =>
        AuthorizeOrderCoreAsync(paypalOrderId, new { card = CardBody(card) }, requestId, cancellationToken);

    public Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string paypalOrderId, string vaultId,
        string requestId, CancellationToken cancellationToken) =>
        AuthorizeOrderCoreAsync(paypalOrderId, new
        {
            card = new
            {
                vault_id = vaultId,
                stored_credential = new
                {
                    payment_initiator = "CUSTOMER",
                    payment_type = "ONE_TIME",
                    usage = "SUBSEQUENT"
                }
            }
        }, requestId, cancellationToken);

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        using var json = await SendJsonAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null, cancellationToken);
        return ParseAuthorization(json.RootElement);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string requestId, CancellationToken cancellationToken)
    {
        using var json = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = Money(amount) }, requestId, cancellationToken);
        return ParseAuthorization(json.RootElement);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount,
        string requestId, CancellationToken cancellationToken)
    {
        using var json = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new { amount = Money(amount), final_capture = true }, requestId, cancellationToken);
        var root = json.RootElement;
        return ParseCapture(root);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        using var json = await SendJsonAsync(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null, cancellationToken);
        return ParseCapture(json.RootElement);
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            null, requestId, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string requestId,
        CancellationToken cancellationToken)
    {
        using var json = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            amount is null ? new { } : new { amount = Money(amount.Value) }, requestId, cancellationToken);
        var root = json.RootElement;
        return new PayPalRefundResult(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            MoneyValue(root, "amount") ?? throw new JsonException("PayPal refund amount was missing."),
            DateValue(root, "create_time"));
    }

    public async Task<PayPalPaymentTokenResult> CreatePaymentTokenAsync(string customerId, PayPalCard card,
        string requestId, CancellationToken cancellationToken)
    {
        var body = new
        {
            payment_source = new { card = CardBody(card) },
            customer = new { merchant_customer_id = customerId }
        };
        using var json = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, requestId,
            cancellationToken);
        var root = json.RootElement;
        var cardElement = root.GetProperty("payment_source").GetProperty("card");
        return new PayPalPaymentTokenResult(
            RequiredString(root, "id"),
            RequiredString(cardElement, "brand"),
            RequiredString(cardElement, "last_digits"),
            RequiredString(cardElement, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}", null, null, cancellationToken);
    }

    public async Task<PayPalTransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var transactions = new List<PayPalTransaction>();
        DateTimeOffset? lastRefreshed = null;
        var requestedFrom = from.ToUniversalTime();
        var requestedTo = to.ToUniversalTime();
        var chunkStart = FloorToSecond(requestedFrom);
        var rangeEnd = CeilingToSecond(requestedTo);

        while (chunkStart <= rangeEnd)
        {
            var maximumChunkEnd = chunkStart.AddDays(31).AddSeconds(-1);
            var chunkEnd = maximumChunkEnd < rangeEnd ? maximumChunkEnd : rangeEnd;
            var page = 1;
            var hasMorePages = true;
            do
            {
                var query = $"/v1/reporting/transactions?start_date={EncodeDate(chunkStart)}" +
                            $"&end_date={EncodeDate(chunkEnd)}&fields=transaction_info" +
                            $"&balance_affecting_records_only=N&page_size=500&page={page}";
                using var json = await SendJsonAsync(HttpMethod.Get, query, null, null, cancellationToken);
                var root = json.RootElement;
                lastRefreshed = DateValue(root, "last_refreshed_datetime") ?? lastRefreshed;
                var pageItemCount = 0;
                if (root.TryGetProperty("transaction_details", out var details))
                {
                    pageItemCount = details.GetArrayLength();
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (!detail.TryGetProperty("transaction_info", out var info)) continue;
                        transactions.Add(ParseTransaction(info));
                    }
                }

                hasMorePages = root.TryGetProperty("total_pages", out var pages)
                    ? page < pages.GetInt32()
                    : pageItemCount == 500;
                page++;
            } while (hasMorePages);

            if (chunkEnd == rangeEnd) break;
            chunkStart = chunkEnd.AddSeconds(1);
        }

        var exactRangeTransactions = transactions
            .Where(x => x.InitiatedAt is null || (x.InitiatedAt >= requestedFrom && x.InitiatedAt <= requestedTo))
            .ToList();
        return new PayPalTransactionSearchResult(exactRangeTransactions, lastRefreshed);
    }

    private async Task<PayPalAuthorizationResult> AuthorizeOrderCoreAsync(string paypalOrderId,
        object paymentSource, string requestId, CancellationToken cancellationToken)
    {
        using var json = await SendJsonAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(paypalOrderId)}/authorize",
            new { payment_source = paymentSource }, requestId, cancellationToken);
        ThrowIfChallenge(json.RootElement);
        var authorization = json.RootElement.GetProperty("purchase_units")[0]
            .GetProperty("payments").GetProperty("authorizations")[0];
        return ParseAuthorization(authorization);
    }

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken)
    {
        var response = await SendAsync(method, path, body, requestId, cancellationToken);
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            ThrowIfChallenge(json.RootElement);
            response.Dispose();
            return json;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(false, cancellationToken);
        var response = await SendOnceAsync(method, path, body, requestId, token, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            token = await GetAccessTokenAsync(true, cancellationToken);
            response = await SendOnceAsync(method, path, body, requestId, token, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            var exception = await CreateExceptionAsync(response, cancellationToken);
            response.Dispose();
            throw exception;
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, object? body,
        string? requestId, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUrl(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (requestId is not null) request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return _accessToken;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
                return _accessToken;

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl("/v1/oauth2/token"));
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{Require(_options.ClientId, "PayPal:ClientId")}:{Require(_options.ClientSecret, "PayPal:ClientSecret")}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            _accessToken = RequiredString(json.RootElement, "access_token");
            var expiresIn = json.RootElement.TryGetProperty("expires_in", out var expiry) ? expiry.GetInt32() : 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<PayPalApiException> CreateExceptionAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = json.RootElement;
            var debugId = OptionalString(root, "debug_id");
            var message = OptionalString(root, "message") ?? $"PayPal returned HTTP {(int)response.StatusCode}.";
            string? issue = null;
            string? description = null;
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array &&
                details.GetArrayLength() > 0)
            {
                issue = OptionalString(details[0], "issue");
                description = OptionalString(details[0], "description");
            }

            if (string.Equals(issue, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
                return new PayPalChallengeRequiredException(debugId);

            var safeMessage = description is null ? message : $"{message} {description}";
            return new PayPalApiException(response.StatusCode, safeMessage, debugId, issue);
        }
        catch (PayPalApiException)
        {
            throw;
        }
        catch
        {
            return new PayPalApiException(response.StatusCode,
                $"PayPal returned HTTP {(int)response.StatusCode}.");
        }
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
            admin_area_2 = card.BillingAddress.AdminArea2,
            admin_area_1 = card.BillingAddress.AdminArea1,
            postal_code = card.BillingAddress.PostalCode,
            country_code = card.BillingAddress.CountryCode.ToUpperInvariant()
        }
    };

    private object Money(decimal amount) => new
    {
        currency_code = Currency,
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static PayPalAuthorizationResult ParseAuthorization(JsonElement root) => new(
        RequiredString(root, "id"),
        RequiredString(root, "status"),
        MoneyValue(root, "amount") ?? throw new JsonException("PayPal authorization amount was missing."),
        MoneyCurrency(root, "amount") ?? throw new JsonException("PayPal authorization currency was missing."),
        DateValue(root, "create_time"),
        DateValue(root, "update_time"),
        DateValue(root, "expiration_time"));

    private static PayPalCaptureResult ParseCapture(JsonElement root)
    {
        var breakdown = OptionalObject(root, "seller_receivable_breakdown");
        return new PayPalCaptureResult(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            MoneyValue(root, "amount") ?? throw new JsonException("PayPal capture amount was missing."),
            MoneyCurrency(root, "amount") ?? throw new JsonException("PayPal capture currency was missing."),
            breakdown is null ? null : MoneyValue(breakdown.Value, "paypal_fee"),
            breakdown is null ? null : MoneyValue(breakdown.Value, "net_amount"),
            DateValue(root, "create_time"));
    }

    private static PayPalTransaction ParseTransaction(JsonElement info) => new(
        RequiredString(info, "transaction_id"),
        OptionalString(info, "paypal_reference_id"),
        OptionalString(info, "transaction_event_code"),
        OptionalString(info, "transaction_status"),
        DateValue(info, "transaction_initiation_date"),
        DateValue(info, "transaction_updated_date"),
        MoneyValue(info, "transaction_amount"),
        MoneyCurrency(info, "transaction_amount"),
        MoneyValue(info, "fee_amount"),
        OptionalString(info, "invoice_id"),
        OptionalString(info, "custom_field"));

    private static void ThrowIfChallenge(JsonElement root)
    {
        if (string.Equals(OptionalString(root, "status"), "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            throw new PayPalChallengeRequiredException();
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array &&
            links.EnumerateArray().Any(x => string.Equals(OptionalString(x, "rel"), "payer-action",
                StringComparison.OrdinalIgnoreCase)))
            throw new PayPalChallengeRequiredException();
    }

    private static string RequiredString(JsonElement element, string property) =>
        OptionalString(element, property) ?? throw new JsonException($"PayPal response omitted '{property}'.");

    private static string? OptionalString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static JsonElement? OptionalObject(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static decimal? MoneyValue(JsonElement element, string property)
    {
        var money = OptionalObject(element, property);
        var value = money is null ? null : OptionalString(money.Value, "value");
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static string? MoneyCurrency(JsonElement element, string property)
    {
        var money = OptionalObject(element, property);
        return money is null ? null : OptionalString(money.Value, "currency_code");
    }

    private static DateTimeOffset? DateValue(JsonElement element, string property) =>
        DateTimeOffset.TryParse(OptionalString(element, property), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;

    private static string EncodeDate(DateTimeOffset value) =>
        Uri.EscapeDataString(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

    private static DateTimeOffset FloorToSecond(DateTimeOffset value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);

    private static DateTimeOffset CeilingToSecond(DateTimeOffset value)
    {
        var floor = FloorToSecond(value);
        return floor == value ? floor : floor.AddSeconds(1);
    }

    private string BuildUrl(string path) => $"{_baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private static string ResolveBaseUrl(PayPalOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
                throw new InvalidOperationException("PayPal:BaseUrl must be an absolute URL.");
            return options.BaseUrl;
        }

        return Require(options.Environment, "PayPal:Environment").ToUpperInvariant() switch
        {
            "SANDBOX" => "https://api-m.sandbox.paypal.com",
            "LIVE" => "https://api-m.paypal.com",
            _ => throw new InvalidOperationException("PayPal:Environment must be Sandbox or Live.")
        };
    }

    private static string Require(string? value, string setting) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidOperationException($"{setting} is required.");
}

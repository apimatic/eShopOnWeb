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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// A deliberately small client implemented from the PayPal OpenAPI documents in api-specs/paypal.
/// No PayPal SDK is used.
/// </summary>
public sealed class PayPalGateway : IPayPalGateway
{
    private const string AccessTokenCacheKey = "paypal-access-token";
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly PayPalOptions _options;

    public PayPalGateway(IHttpClientFactory httpClientFactory, IMemoryCache cache,
        IOptions<PayPalOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _options = options.Value;
    }

    public async Task<PayPalOrderResult> CreateOrderAsync(string orderReference, decimal amount,
        string currency, CancellationToken cancellationToken)
    {
        var externalId = orderReference;
        var payload = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = externalId,
                    invoice_id = externalId,
                    custom_id = externalId,
                    amount = Money(amount, currency)
                }
            }
        };

        using var json = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", payload,
            $"{externalId}-create", cancellationToken);
        return ParseOrder(json.RootElement);
    }

    public async Task<PayPalOrderResult> GetOrderAsync(string payPalOrderId,
        CancellationToken cancellationToken)
    {
        using var json = await SendJsonAsync(HttpMethod.Get,
            $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}", null, null, cancellationToken);
        return ParseOrder(json.RootElement);
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string orderReference,
        string payPalOrderId, CardPaymentSource? card, string? vaultId,
        CancellationToken cancellationToken)
    {
        if ((card == null) == string.IsNullOrWhiteSpace(vaultId))
            throw new ArgumentException("Exactly one card source or vault ID is required.");

        object cardPayload = card != null
            ? CardPayload(card)
            : new
            {
                vault_id = vaultId,
                stored_credential = new
                {
                    payment_initiator = "CUSTOMER",
                    payment_type = "ONE_TIME",
                    usage = "SUBSEQUENT"
                }
            };

        var payload = new { payment_source = new { card = cardPayload } };
        using var json = await SendJsonAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize", payload,
            $"{orderReference}-authorize", cancellationToken);

        ThrowIfPayerActionRequired(json.RootElement);
        var order = ParseOrder(json.RootElement);
        return order.Authorization
            ?? throw InvalidResponse("The PayPal authorization response did not contain an authorization.");
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        using var json = await SendJsonAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null,
            cancellationToken);
        return ParseAuthorization(json.RootElement, string.Empty);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string orderReference,
        string authorizationId, decimal amount, string currency, CancellationToken cancellationToken)
    {
        var payload = new { amount = Money(amount, currency) };
        using var json = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            payload, $"{orderReference}-reauthorize", cancellationToken);
        return ParseAuthorization(json.RootElement, string.Empty);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string orderReference, string authorizationId,
        decimal amount, string currency, CancellationToken cancellationToken)
    {
        var payload = new
        {
            amount = Money(amount, currency),
            invoice_id = orderReference,
            final_capture = true
        };
        using var json = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            payload, $"{orderReference}-capture", cancellationToken);
        return ParseCapture(json.RootElement);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId,
        CancellationToken cancellationToken)
    {
        using var json = await SendJsonAsync(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null,
            cancellationToken);
        return ParseCapture(json.RootElement);
    }

    public async Task VoidAsync(string orderReference, string authorizationId,
        CancellationToken cancellationToken)
    {
        await SendNoContentAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            $"{orderReference}-void", cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundAsync(string orderReference, string captureId,
        string idempotencyKey, decimal amount, string currency, string? note,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            amount = Money(amount, currency),
            custom_id = orderReference,
            note_to_payer = string.IsNullOrWhiteSpace(note) ? null : note
        };
        using var json = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund", payload,
            idempotencyKey, cancellationToken);
        return ParseRefund(json.RootElement);
    }

    public async Task<SavedCardResult> SaveCardAsync(string merchantCustomerId,
        CardPaymentSource card, CancellationToken cancellationToken)
    {
        var setupPayload = new
        {
            customer = new { merchant_customer_id = merchantCustomerId },
            payment_source = new { card = CardPayload(card) }
        };
        var setupRequestId = $"eshop-setup-{Guid.NewGuid():N}";
        using var setupJson = await SendJsonAsync(HttpMethod.Post, "/v3/vault/setup-tokens",
            setupPayload, setupRequestId, cancellationToken);
        ThrowIfPayerActionRequired(setupJson.RootElement);

        var setupId = RequiredString(setupJson.RootElement, "id");
        var setupStatus = OptionalString(setupJson.RootElement, "status");
        if (!string.Equals(setupStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalApiException(HttpStatusCode.Conflict, "SETUP_TOKEN_NOT_APPROVED",
                $"PayPal returned setup-token status '{setupStatus ?? "UNKNOWN"}', so the card cannot be saved.");
        }

        var tokenPayload = new
        {
            customer = new { merchant_customer_id = merchantCustomerId },
            payment_source = new
            {
                token = new { id = setupId, type = "SETUP_TOKEN" }
            }
        };
        using var tokenJson = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens",
            tokenPayload, $"eshop-vault-{setupId}", cancellationToken);

        var root = tokenJson.RootElement;
        var cardElement = RequiredProperty(RequiredProperty(root, "payment_source"), "card");
        return new SavedCardResult(
            RequiredString(root, "id"),
            RequiredString(cardElement, "brand"),
            RequiredString(cardElement, "last_digits"),
            OptionalString(cardElement, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken)
    {
        await SendNoContentAsync(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}", null, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransactionResult>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<PayPalTransactionResult>();
        var chunkStart = from.ToUniversalTime();
        var absoluteEnd = to.ToUniversalTime();

        while (chunkStart < absoluteEnd)
        {
            var chunkEnd = chunkStart.AddDays(31);
            if (chunkEnd > absoluteEnd) chunkEnd = absoluteEnd;

            var page = 1;
            var totalPages = 1;
            do
            {
                var query = $"?start_date={EncodeDate(chunkStart)}&end_date={EncodeDate(chunkEnd)}" +
                            $"&fields=transaction_info&balance_affecting_records_only=N&page_size=500&page={page}";
                using var json = await SendJsonAsync(HttpMethod.Get,
                    "/v1/reporting/transactions" + query, null, null, cancellationToken);
                var root = json.RootElement;
                if (root.TryGetProperty("transaction_details", out var details))
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (!detail.TryGetProperty("transaction_info", out var info)) continue;
                        results.Add(ParseTransaction(info));
                    }
                }

                totalPages = OptionalInt32(root, "total_pages") ?? 1;
                page++;
            } while (page <= totalPages);

            chunkStart = chunkEnd;
        }

        return results
            .GroupBy(x => new { x.TransactionId, x.EventCode, x.InitiatedAt })
            .Select(x => x.First())
            .ToList();
    }

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, object? payload,
        string? requestId, CancellationToken cancellationToken)
    {
        var response = await SendAsync(method, path, payload, requestId, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        finally
        {
            response.Dispose();
        }
    }

    private async Task SendNoContentAsync(HttpMethod method, string path, string? requestId,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, null, requestId, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path,
        object? payload, string? requestId, CancellationToken cancellationToken)
    {
        _options.Validate();
        string token;
        HttpResponseMessage response;
        try
        {
            token = await GetAccessTokenAsync(cancellationToken);
            response = await SendOnceAsync(method, path, payload, requestId, token, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw PayPalUnavailable();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw PayPalUnavailable();
        }
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            _cache.Remove(AccessTokenCacheKey);
            try
            {
                token = await GetAccessTokenAsync(cancellationToken);
                response = await SendOnceAsync(method, path, payload, requestId, token, cancellationToken);
            }
            catch (HttpRequestException)
            {
                throw PayPalUnavailable();
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw PayPalUnavailable();
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            var exception = await CreateExceptionAsync(response, cancellationToken);
            response.Dispose();
            throw exception;
        }
        return response;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path,
        object? payload, string? requestId, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrWhiteSpace(requestId))
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        if (payload != null)
            request.Content = JsonContent.Create(payload, options: JsonOptions);

        return await _httpClientFactory.CreateClient("PayPal").SendAsync(request, cancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<string>(AccessTokenCacheKey, out var cached) && cached != null)
            return cached;

        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue<string>(AccessTokenCacheKey, out cached) && cached != null)
                return cached;

            _options.Validate();
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes(
                $"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _httpClientFactory.CreateClient("PayPal")
                .SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw await CreateExceptionAsync(response, cancellationToken);

            using var json = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            var accessToken = RequiredString(json.RootElement, "access_token");
            var expiresIn = OptionalInt32(json.RootElement, "expires_in") ?? 300;
            _cache.Set(AccessTokenCacheKey, accessToken,
                TimeSpan.FromSeconds(Math.Max(30, expiresIn - 60)));
            return accessToken;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private Uri BuildUri(string path)
    {
        var baseUrl = _options.GetBaseUri().ToString().TrimEnd('/');
        return new Uri(baseUrl + "/" + path.TrimStart('/'), UriKind.Absolute);
    }

    private static async Task<PayPalApiException> CreateExceptionAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string name = "PAYPAL_ERROR";
        string message = $"PayPal returned HTTP {(int)response.StatusCode}.";
        string? debugId = null;
        string? issue = null;
        try
        {
            using var json = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            var root = json.RootElement;
            name = OptionalString(root, "name") ?? name;
            message = OptionalString(root, "message") ?? message;
            debugId = OptionalString(root, "debug_id");
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
            // The OpenAPI error schema is expected, but never surface an unstructured body.
        }

        if (string.Equals(name, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(issue, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            return new PayPalPayerActionRequiredException(debugId);

        return new PayPalApiException(response.StatusCode, name, message, debugId, issue);
    }

    private static PayPalOrderResult ParseOrder(JsonElement root)
    {
        ThrowIfPayerActionRequired(root);
        var status = RequiredString(root, "status");
        PayPalAuthorizationResult? authorization = null;
        PayPalCaptureResult? capture = null;
        if (root.TryGetProperty("purchase_units", out var units))
        {
            foreach (var unit in units.EnumerateArray())
            {
                if (!unit.TryGetProperty("payments", out var payments)) continue;
                if (authorization == null && payments.TryGetProperty("authorizations", out var authorizations))
                {
                    var first = authorizations.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Object)
                        authorization = ParseAuthorization(first, status);
                }
                if (capture == null && payments.TryGetProperty("captures", out var captures))
                {
                    var first = captures.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Object)
                        capture = ParseCapture(first);
                }
            }
        }
        return new PayPalOrderResult(RequiredString(root, "id"), status, authorization, capture);
    }

    private static PayPalAuthorizationResult ParseAuthorization(JsonElement root, string orderStatus)
    {
        var amount = RequiredProperty(root, "amount");
        return new PayPalAuthorizationResult(
            RequiredString(root, "id"), RequiredString(root, "status"),
            RequiredDecimal(amount, "value"), RequiredString(amount, "currency_code"),
            OptionalDate(root, "create_time"), OptionalDate(root, "expiration_time"), orderStatus);
    }

    private static PayPalCaptureResult ParseCapture(JsonElement root)
    {
        var amount = RequiredProperty(root, "amount");
        decimal? fee = null;
        decimal? net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = OptionalMoneyValue(breakdown, "paypal_fee");
            net = OptionalMoneyValue(breakdown, "net_amount");
        }
        return new PayPalCaptureResult(
            RequiredString(root, "id"), RequiredString(root, "status"),
            RequiredDecimal(amount, "value"), RequiredString(amount, "currency_code"),
            fee, net, OptionalDate(root, "create_time"));
    }

    private static PayPalRefundResult ParseRefund(JsonElement root)
    {
        var amount = RequiredProperty(root, "amount");
        decimal? fee = null;
        decimal? net = null;
        if (root.TryGetProperty("seller_payable_breakdown", out var breakdown))
        {
            fee = OptionalMoneyValue(breakdown, "paypal_fee");
            net = OptionalMoneyValue(breakdown, "net_amount");
        }
        return new PayPalRefundResult(
            RequiredString(root, "id"), RequiredString(root, "status"),
            RequiredDecimal(amount, "value"), RequiredString(amount, "currency_code"),
            fee, net, OptionalDate(root, "create_time"));
    }

    private static PayPalTransactionResult ParseTransaction(JsonElement info)
    {
        var gross = OptionalMoneyValue(info, "transaction_amount");
        var fee = OptionalMoneyValue(info, "fee_amount");
        string? currency = null;
        if (info.TryGetProperty("transaction_amount", out var amount))
            currency = OptionalString(amount, "currency_code");
        return new PayPalTransactionResult(
            RequiredString(info, "transaction_id"),
            OptionalString(info, "paypal_reference_id"),
            OptionalString(info, "paypal_reference_id_type"),
            OptionalString(info, "invoice_id"),
            OptionalString(info, "custom_field"),
            OptionalString(info, "transaction_event_code"),
            OptionalString(info, "transaction_status"),
            gross, fee, currency,
            OptionalDate(info, "transaction_initiation_date"),
            OptionalDate(info, "transaction_updated_date"));
    }

    private static object CardPayload(CardPaymentSource card) => new
    {
        name = card.Name,
        number = new string(card.Number.Where(char.IsDigit).ToArray()),
        expiry = card.Expiry,
        security_code = card.SecurityCode,
        billing_address = new
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
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static string EncodeDate(DateTimeOffset value) => Uri.EscapeDataString(
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

    private static void ThrowIfPayerActionRequired(JsonElement root)
    {
        if (string.Equals(OptionalString(root, "status"), "PAYER_ACTION_REQUIRED",
            StringComparison.OrdinalIgnoreCase))
            throw new PayPalPayerActionRequiredException();
    }

    private static PayPalApiException InvalidResponse(string message) =>
        new(HttpStatusCode.BadGateway, "INVALID_PAYPAL_RESPONSE", message);

    private static PayPalApiException PayPalUnavailable() =>
        new(HttpStatusCode.ServiceUnavailable, "PAYPAL_UNAVAILABLE",
            "PayPal could not be reached. Retry the same operation so its idempotency key is reused.");

    private static JsonElement RequiredProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value : throw InvalidResponse(
            $"PayPal's response omitted required field '{name}'.");

    private static string RequiredString(JsonElement element, string name) =>
        OptionalString(element, name) ?? throw InvalidResponse(
            $"PayPal's response omitted required field '{name}'.");

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? OptionalInt32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static decimal RequiredDecimal(JsonElement element, string name)
    {
        var value = RequiredString(element, name);
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            throw InvalidResponse($"PayPal's response field '{name}' was not a decimal amount.");
        return parsed;
    }

    private static decimal? OptionalMoneyValue(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var money)) return null;
        var value = OptionalString(money, "value");
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : null;
    }

    private static DateTimeOffset? OptionalDate(JsonElement element, string name)
    {
        var value = OptionalString(element, name);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;
    }
}

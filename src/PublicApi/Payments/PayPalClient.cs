using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalClient : IPayPalClient
{
    public const string HttpClientName = "PayPal";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly Uri _baseUri;
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalClient(IHttpClientFactory httpClientFactory, IOptions<PayPalOptions> options,
        ILogger<PayPalClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _baseUri = ResolveBaseUri(_options);
    }

    public async Task<PayPalOrderResult> CreateOrderAsync(int orderId, string invoiceId,
        string referenceId, decimal amount, string currency, IReadOnlyCollection<PayPalOrderItem> items,
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
                    invoice_id = invoiceId,
                    custom_id = referenceId,
                    description = $"eShopOnWeb order {orderId}",
                    amount = new
                    {
                        currency_code = currency,
                        value = Money(amount),
                        breakdown = new
                        {
                            item_total = new { currency_code = currency, value = Money(amount) }
                        }
                    },
                    items = items.Select(x => new
                    {
                        name = x.Name.Length <= 127 ? x.Name : x.Name[..127],
                        unit_amount = new { currency_code = currency, value = Money(x.UnitPrice) },
                        quantity = x.Quantity.ToString(CultureInfo.InvariantCulture),
                        sku = x.CatalogItemId.ToString(CultureInfo.InvariantCulture),
                        category = "PHYSICAL_GOODS"
                    })
                }
            }
        };

        var json = await SendAsync(HttpMethod.Post, "v2/checkout/orders", body, requestId, true,
            cancellationToken);
        return new PayPalOrderResult(RequiredString(json, "id"), RequiredString(json, "status"));
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string payPalOrderId,
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

        var json = await SendAsync(HttpMethod.Post,
            $"v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize",
            new { payment_source = new { card = cardSource } }, requestId, true, cancellationToken);
        ThrowIfPayerActionRequired(json);

        if (!json.TryGetProperty("purchase_units", out var units) || units.GetArrayLength() == 0 ||
            !units[0].TryGetProperty("payments", out var payments) ||
            !payments.TryGetProperty("authorizations", out var authorizations) ||
            authorizations.GetArrayLength() == 0)
        {
            throw new PayPalApiException(HttpStatusCode.Conflict, "AUTHORIZATION_NOT_RETURNED",
                "PayPal did not return an authorization for the order.", OptionalString(json, "debug_id"));
        }

        return ParseAuthorization(authorizations[0]);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Get,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null, true,
            cancellationToken);
        return ParseAuthorization(json);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId,
        decimal amount, string currency, string requestId, CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = new { currency_code = currency, value = Money(amount) } }, requestId, true,
            cancellationToken);
        return ParseAuthorization(json);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount,
        string currency, string invoiceId, string requestId, CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new
            {
                amount = new { currency_code = currency, value = Money(amount) },
                invoice_id = invoiceId,
                final_capture = true
            }, requestId, true, cancellationToken);
        return ParseCapture(json);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId,
        CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Get,
            $"v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null, true,
            cancellationToken);
        return ParseCapture(json);
    }

    public async Task VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        await SendAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void", null,
            requestId, true, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount,
        string currency, string invoiceId, string referenceId, string? note, string requestId,
        CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Post,
            $"v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new
            {
                amount = new { currency_code = currency, value = Money(amount) },
                invoice_id = invoiceId,
                custom_id = referenceId,
                note_to_payer = string.IsNullOrWhiteSpace(note) ? null : note
            }, requestId, true, cancellationToken);
        return ParseRefund(json);
    }

    public async Task<PayPalVaultedCardResult> CreatePaymentTokenAsync(string merchantCustomerId,
        PayPalCard card, string requestId, CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Post, "v3/vault/payment-tokens",
            new
            {
                customer = new { merchant_customer_id = merchantCustomerId },
                payment_source = new { card = CardBody(card) }
            }, requestId, true, cancellationToken);
        ThrowIfPayerActionRequired(json);
        var source = RequiredProperty(RequiredProperty(json, "payment_source"), "card");
        return new PayPalVaultedCardResult(RequiredString(json, "id"), RequiredString(source, "brand"),
            RequiredString(source, "last_digits"), RequiredString(source, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken)
    {
        await SendAsync(HttpMethod.Delete,
            $"v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}", null, null, true,
            cancellationToken);
    }

    public async Task<PayPalTransactionPage> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, int page, int pageSize, CancellationToken cancellationToken)
    {
        var path = "v1/reporting/transactions" +
                   $"?start_date={Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}" +
                   $"&end_date={Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}" +
                   $"&fields=transaction_info&balance_affecting_records_only=N&page_size={pageSize}&page={page}";
        var json = await SendAsync(HttpMethod.Get, path, null, null, true, cancellationToken);
        var results = new List<PayPalTransactionResult>();
        if (json.TryGetProperty("transaction_details", out var details))
        {
            foreach (var detail in details.EnumerateArray())
            {
                if (!detail.TryGetProperty("transaction_info", out var info)) continue;
                var amount = RequiredProperty(info, "transaction_amount");
                var fee = info.TryGetProperty("fee_amount", out var feeValue)
                    ? OptionalDecimal(feeValue, "value")
                    : null;
                results.Add(new PayPalTransactionResult(
                    RequiredString(info, "transaction_id"),
                    OptionalString(info, "paypal_reference_id"),
                    OptionalString(info, "transaction_event_code"),
                    OptionalString(info, "transaction_status"),
                    OptionalDate(info, "transaction_initiation_date"),
                    RequiredDecimal(amount, "value"),
                    RequiredString(amount, "currency_code"),
                    fee,
                    OptionalString(info, "invoice_id"),
                    OptionalString(info, "custom_field")));
            }
        }

        return new PayPalTransactionPage(results,
            OptionalInt(json, "page") ?? page,
            OptionalInt(json, "total_pages") ?? (results.Count < pageSize ? page : page + 1));
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body,
        string? requestId, bool retryable, CancellationToken cancellationToken)
    {
        var serializedBody = body is null ? null : JsonSerializer.Serialize(body, JsonOptions);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var token = await GetAccessTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (!string.IsNullOrEmpty(requestId))
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            if (serializedBody is not null)
                request.Content = new StringContent(serializedBody, Encoding.UTF8, "application/json");

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                InvalidateAccessToken();
                continue;
            }

            if (retryable && attempt < 2 &&
                (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * (1 << attempt) + Random.Shared.Next(100)),
                    cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
                throw ParseException(response.StatusCode, responseText);

            if (string.IsNullOrWhiteSpace(responseText))
                return JsonDocument.Parse("{}").RootElement.Clone();

            using var document = JsonDocument.Parse(responseText);
            return document.RootElement.Clone();
        }

        throw new PayPalApiException(HttpStatusCode.ServiceUnavailable, "PAYPAL_RETRY_EXHAUSTED",
            "PayPal did not complete the request after safe retries.", null);
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

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, "v1/oauth2/token"));
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw ParseException(response.StatusCode, responseText);

            using var document = JsonDocument.Parse(responseText);
            _accessToken = RequiredString(document.RootElement, "access_token");
            var expiresIn = OptionalInt(document.RootElement, "expires_in") ?? 300;
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
        _accessTokenExpiresAt = default;
    }

    private PayPalApiException ParseException(HttpStatusCode statusCode, string responseText)
    {
        string code = "PAYPAL_ERROR";
        string message = $"PayPal returned HTTP {(int)statusCode}.";
        string? debugId = null;
        string? issue = null;
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            code = OptionalString(root, "name") ?? code;
            message = OptionalString(root, "message") ?? message;
            debugId = OptionalString(root, "debug_id");
            if (root.TryGetProperty("details", out var details) && details.GetArrayLength() > 0)
            {
                issue = OptionalString(details[0], "issue");
                message = OptionalString(details[0], "description") ?? message;
            }
        }
        catch (JsonException)
        {
            // PayPal occasionally returns an empty or non-JSON gateway response.
        }

        _logger.LogWarning("PayPal request failed with {StatusCode}, code {Code}, issue {Issue}, debug ID {DebugId}",
            (int)statusCode, code, issue, debugId);
        return new PayPalApiException(statusCode, code, message, debugId, issue);
    }

    private static object CardBody(PayPalCard card) => new
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

    private static PayPalAuthorizationResult ParseAuthorization(JsonElement json)
    {
        var amount = RequiredProperty(json, "amount");
        return new PayPalAuthorizationResult(RequiredString(json, "id"), RequiredString(json, "status"),
            RequiredDecimal(amount, "value"), RequiredString(amount, "currency_code"),
            OptionalDate(json, "create_time"), OptionalDate(json, "expiration_time"));
    }

    private static PayPalCaptureResult ParseCapture(JsonElement json)
    {
        var amount = RequiredProperty(json, "amount");
        decimal? fee = null;
        decimal? net = null;
        if (json.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            if (breakdown.TryGetProperty("paypal_fee", out var feeMoney)) fee = OptionalDecimal(feeMoney, "value");
            if (breakdown.TryGetProperty("net_amount", out var netMoney)) net = OptionalDecimal(netMoney, "value");
        }

        return new PayPalCaptureResult(RequiredString(json, "id"), RequiredString(json, "status"),
            RequiredDecimal(amount, "value"), RequiredString(amount, "currency_code"), fee, net,
            OptionalDate(json, "create_time"));
    }

    private static PayPalRefundResult ParseRefund(JsonElement json)
    {
        var amount = RequiredProperty(json, "amount");
        decimal? fee = null;
        decimal? net = null;
        if (json.TryGetProperty("seller_payable_breakdown", out var breakdown))
        {
            if (breakdown.TryGetProperty("paypal_fee", out var feeMoney)) fee = OptionalDecimal(feeMoney, "value");
            if (breakdown.TryGetProperty("net_amount", out var netMoney)) net = OptionalDecimal(netMoney, "value");
        }

        return new PayPalRefundResult(RequiredString(json, "id"), RequiredString(json, "status"),
            RequiredDecimal(amount, "value"), RequiredString(amount, "currency_code"), fee, net);
    }

    private static void ThrowIfPayerActionRequired(JsonElement json)
    {
        if (string.Equals(OptionalString(json, "status"), "PAYER_ACTION_REQUIRED",
                StringComparison.OrdinalIgnoreCase) ||
            json.TryGetProperty("links", out var links) && links.EnumerateArray().Any(link =>
                string.Equals(OptionalString(link, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase)))
        {
            throw new PayPalApiException(HttpStatusCode.Conflict, "PAYER_ACTION_REQUIRED",
                "PayPal requires a browser approval or card challenge for this request.",
                OptionalString(json, "debug_id"));
        }
    }

    private static Uri ResolveBaseUri(PayPalOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            return new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);

        return options.Environment.Trim().ToLowerInvariant() switch
        {
            "sandbox" => new Uri("https://api-m.sandbox.paypal.com/"),
            "live" or "production" => new Uri("https://api-m.paypal.com/"),
            _ => throw new InvalidOperationException(
                "PayPal:Environment must be Sandbox, Live, or Production when PayPal:BaseUrl is not set.")
        };
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static JsonElement RequiredProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value : throw new JsonException($"PayPal response omitted {name}.");
    private static string RequiredString(JsonElement element, string name) =>
        OptionalString(element, name) ?? throw new JsonException($"PayPal response omitted {name}.");
    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? OptionalInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;
    private static decimal RequiredDecimal(JsonElement element, string name) =>
        OptionalDecimal(element, name) ?? throw new JsonException($"PayPal response omitted {name}.");
    private static decimal? OptionalDecimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && decimal.TryParse(value.GetString(), NumberStyles.Number,
            CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    private static DateTimeOffset? OptionalDate(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && DateTimeOffset.TryParse(value.GetString(),
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
}

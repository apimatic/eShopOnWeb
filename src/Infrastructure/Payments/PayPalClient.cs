using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private static readonly ConcurrentDictionary<string, AccessTokenCache> TokenCaches = new();
    private readonly AccessTokenCache _tokenCache;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _tokenCache = TokenCaches.GetOrAdd($"{_options.ClientId}|{ApiBaseUrl()}", _ => new AccessTokenCache());
    }

    public async Task<string> CreateOrderAsync(Guid externalId, decimal amount, string currency,
        IReadOnlyCollection<PayPalLineItem> items, string requestId, CancellationToken cancellationToken)
    {
        var money = FormatMoney(amount);
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = externalId.ToString("N"),
                    invoice_id = InvoiceId(externalId),
                    custom_id = externalId.ToString("N"),
                    amount = new
                    {
                        currency_code = currency,
                        value = money,
                        breakdown = new { item_total = new { currency_code = currency, value = money } }
                    },
                    items = items.Select(x => new
                    {
                        name = x.Name,
                        sku = x.CatalogItemId.ToString(CultureInfo.InvariantCulture),
                        unit_amount = new { currency_code = currency, value = FormatMoney(x.UnitPrice) },
                        quantity = x.Quantity.ToString(CultureInfo.InvariantCulture),
                        category = "PHYSICAL_GOODS"
                    })
                }
            }
        };

        using var document = (await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId, cancellationToken))!;
        return RequiredString(document.RootElement, "id");
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string paypalOrderId, PayPalCard? card,
        string? vaultId, string requestId, CancellationToken cancellationToken)
    {
        object cardSource = card is not null
            ? CardBody(card)
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

        using var document = (await SendJsonAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(paypalOrderId)}/authorize",
            new { payment_source = new { card = cardSource } }, requestId, cancellationToken))!;
        return ParseOrderAuthorization(document.RootElement, paypalOrderId, "payment authorization");
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, string paypalOrderId,
        CancellationToken cancellationToken)
    {
        using var document = (await SendJsonAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null, cancellationToken))!;
        return ParseAuthorization(document.RootElement, paypalOrderId, "COMPLETED");
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, string paypalOrderId,
        decimal amount, string currency, string requestId, CancellationToken cancellationToken)
    {
        using var document = (await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = new { currency_code = currency, value = FormatMoney(amount) } }, requestId, cancellationToken))!;
        return ParseAuthorization(document.RootElement, paypalOrderId, "COMPLETED");
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string invoiceId, string requestId, CancellationToken cancellationToken)
    {
        using var document = (await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new
            {
                amount = new { currency_code = currency, value = FormatMoney(amount) },
                invoice_id = invoiceId,
                final_capture = true
            }, requestId, cancellationToken))!;
        return ParseCapture(document.RootElement);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        using var document = (await SendJsonAsync(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null, cancellationToken))!;
        return ParseCapture(document.RootElement);
    }

    public async Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            null, requestId, cancellationToken, allowNoContent: true);
        return document is null ? "VOIDED" : OptionalString(document.RootElement, "status") ?? "VOIDED";
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, string customId, CancellationToken cancellationToken)
    {
        using var document = (await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new
            {
                amount = new { currency_code = currency, value = FormatMoney(amount) },
                custom_id = customId
            }, requestId, cancellationToken))!;
        return ParseRefund(document.RootElement);
    }

    public async Task<PayPalVaultResult> SaveCardAsync(string ownerId, PayPalCard card, string requestId,
        CancellationToken cancellationToken)
    {
        JsonDocument token;
        try
        {
            token = (await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens",
                new
                {
                    customer = new { merchant_customer_id = ownerId },
                    payment_source = new
                    {
                        card = new
                        {
                            name = card.Name,
                            number = card.Number,
                            expiry = card.Expiry,
                            security_code = card.SecurityCode,
                            billing_address = AddressBody(card.BillingAddress)
                        }
                    }
                }, requestId, cancellationToken))!;
        }
        catch (PayPalApiException ex) when (ex.ErrorName.Contains("PAYER_ACTION", StringComparison.OrdinalIgnoreCase) ||
                                             ex.Issues.Any(x => x.Contains("PAYER_ACTION", StringComparison.OrdinalIgnoreCase)))
        {
            throw new PayPalPayerActionRequiredException("card vaulting");
        }

        using (token)
        {
            var tokenId = RequiredString(token.RootElement, "id");
            var tokenCustomer = token.RootElement.GetProperty("customer");
            var customerId = OptionalString(tokenCustomer, "id")
                ?? throw new PayPalApiException(502, "MISSING_CUSTOMER_ID", "Vault response omitted the customer ID.", null, Array.Empty<string>());
            var tokenCard = token.RootElement.GetProperty("payment_source").GetProperty("card");
            return new PayPalVaultResult(tokenId, customerId, OptionalString(tokenCard, "brand") ?? "UNKNOWN",
                RequiredString(tokenCard, "last_digits"), OptionalString(tokenCard, "expiry") ?? card.Expiry);
        }
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        using var _ = await SendJsonAsync(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}", null, null, cancellationToken,
            allowNoContent: true);
    }

    public async Task<IReadOnlyCollection<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<PayPalTransaction>();
        var page = 1;
        var totalPages = 1;
        do
        {
            var query = $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(FormatDate(from))}" +
                        $"&end_date={Uri.EscapeDataString(FormatDate(to))}&fields=transaction_info" +
                        $"&balance_affecting_records_only=N&page_size=500&page={page}";
            using var document = (await SendJsonAsync(HttpMethod.Get, query, null, null, cancellationToken))!;
            if (document.RootElement.TryGetProperty("transaction_details", out var details))
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (!detail.TryGetProperty("transaction_info", out var info)) continue;
                    results.Add(ParseTransaction(info));
                }
            }
            totalPages = OptionalInt(document.RootElement, "total_pages") ?? 1;
            page++;
        } while (page <= totalPages);

        return results;
    }

    private async Task<JsonDocument?> SendJsonAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken, bool allowNoContent = false)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await GetAccessTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(method, ApiUrl(path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (requestId is not null) request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _tokenCache.AccessToken = null;
                continue;
            }
            if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) &&
                attempt < 2 && (method == HttpMethod.Get || requestId is not null))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150 * (attempt + 1) + Random.Shared.Next(100)), cancellationToken);
                continue;
            }
            if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                if (allowNoContent) return null;
                throw new PayPalApiException(502, "EMPTY_RESPONSE", "PayPal returned no content unexpectedly.", null, Array.Empty<string>());
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_tokenCache.AccessToken is not null && _tokenCache.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return _tokenCache.AccessToken;
        await _tokenCache.Lock.WaitAsync(cancellationToken);
        try
        {
            if (_tokenCache.AccessToken is not null && _tokenCache.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
                return _tokenCache.AccessToken;
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/v1/oauth2/token"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            _tokenCache.AccessToken = RequiredString(document.RootElement, "access_token");
            var expiresIn = OptionalInt(document.RootElement, "expires_in") ?? 300;
            _tokenCache.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _tokenCache.AccessToken;
        }
        finally
        {
            _tokenCache.Lock.Release();
        }
    }

    private string ApiUrl(string path)
    {
        return ApiBaseUrl().TrimEnd('/') + "/" + path.TrimStart('/');
    }

    private string ApiBaseUrl()
    {
        return !string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? _options.BaseUrl!
            : _options.Environment.Equals("Sandbox", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.sandbox.paypal.com"
                : _options.Environment.Equals("Live", StringComparison.OrdinalIgnoreCase)
                    ? "https://api-m.paypal.com"
                    : throw new InvalidOperationException("PayPal:Environment must be Sandbox or Live when PayPal:BaseUrl is not set.");
    }

    private static async Task<PayPalApiException> CreateExceptionAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var issues = new List<string>();
            if (root.TryGetProperty("details", out var details))
            {
                foreach (var detail in details.EnumerateArray())
                {
                    var issue = OptionalString(detail, "issue");
                    if (!string.IsNullOrWhiteSpace(issue)) issues.Add(issue);
                }
            }
            return new PayPalApiException((int)response.StatusCode, OptionalString(root, "name") ?? "API_ERROR",
                OptionalString(root, "message") ?? "The PayPal request failed.", OptionalString(root, "debug_id"), issues);
        }
        catch (JsonException)
        {
            return new PayPalApiException((int)response.StatusCode, "API_ERROR", "The PayPal request failed.", null, Array.Empty<string>());
        }
    }

    private static object CardBody(PayPalCard card) => new
    {
        name = card.Name,
        number = card.Number,
        expiry = card.Expiry,
        security_code = card.SecurityCode,
        billing_address = AddressBody(card.BillingAddress)
    };

    private static object AddressBody(PayPalAddress address) => new
    {
        address_line_1 = address.AddressLine1,
        address_line_2 = address.AddressLine2,
        admin_area_2 = address.City,
        admin_area_1 = address.State,
        postal_code = address.PostalCode,
        country_code = address.CountryCode
    };

    private static PayPalAuthorizationResult ParseOrderAuthorization(JsonElement root, string paypalOrderId, string operation)
    {
        var orderStatus = OptionalString(root, "status") ?? "UNKNOWN";
        if (orderStatus == "PAYER_ACTION_REQUIRED") throw new PayPalPayerActionRequiredException(operation);
        var authorization = root.GetProperty("purchase_units")[0].GetProperty("payments").GetProperty("authorizations")[0];
        return ParseAuthorization(authorization, paypalOrderId, orderStatus);
    }

    private static PayPalAuthorizationResult ParseAuthorization(JsonElement authorization, string paypalOrderId, string orderStatus)
    {
        var amount = authorization.GetProperty("amount");
        return new PayPalAuthorizationResult(paypalOrderId, orderStatus,
            RequiredString(authorization, "id"), RequiredString(authorization, "status"),
            ParseDecimal(amount, "value"), RequiredString(amount, "currency_code"),
            ParseDate(authorization, "create_time") ?? DateTimeOffset.UtcNow,
            ParseDate(authorization, "expiration_time"));
    }

    private static PayPalCaptureResult ParseCapture(JsonElement root)
    {
        var amount = root.GetProperty("amount");
        decimal? fee = null;
        decimal? net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            if (breakdown.TryGetProperty("paypal_fee", out var feeMoney)) fee = ParseDecimal(feeMoney, "value");
            if (breakdown.TryGetProperty("net_amount", out var netMoney)) net = ParseDecimal(netMoney, "value");
        }
        return new PayPalCaptureResult(RequiredString(root, "id"), RequiredString(root, "status"),
            ParseDecimal(amount, "value"), RequiredString(amount, "currency_code"), fee, net,
            ParseDate(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    private static PayPalRefundResult ParseRefund(JsonElement root)
    {
        var amount = root.GetProperty("amount");
        return new PayPalRefundResult(RequiredString(root, "id"), RequiredString(root, "status"),
            ParseDecimal(amount, "value"), RequiredString(amount, "currency_code"),
            ParseDate(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    private static PayPalTransaction ParseTransaction(JsonElement info)
    {
        decimal? amount = null;
        string? currency = null;
        if (info.TryGetProperty("transaction_amount", out var money))
        {
            amount = ParseDecimal(money, "value");
            currency = OptionalString(money, "currency_code");
        }
        decimal? fee = null;
        if (info.TryGetProperty("fee_amount", out var feeMoney)) fee = ParseDecimal(feeMoney, "value");
        return new PayPalTransaction(RequiredString(info, "transaction_id"), OptionalString(info, "paypal_reference_id"),
            OptionalString(info, "paypal_reference_id_type"), OptionalString(info, "transaction_event_code"),
            ParseDate(info, "transaction_initiation_date"), ParseDate(info, "transaction_updated_date"),
            amount, currency, fee, OptionalString(info, "transaction_status"), OptionalString(info, "invoice_id"),
            OptionalString(info, "custom_field"));
    }

    public static string InvoiceId(Guid externalId) => $"eshop-{externalId:N}";
    private static string FormatMoney(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
    private static string FormatDate(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    private static string RequiredString(JsonElement element, string name) =>
        OptionalString(element, name) ?? throw new PayPalApiException(502, "INVALID_RESPONSE", $"PayPal response omitted {name}.", null, Array.Empty<string>());
    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? OptionalInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;
    private static decimal ParseDecimal(JsonElement element, string name) =>
        decimal.Parse(RequiredString(element, name), NumberStyles.Number, CultureInfo.InvariantCulture);
    private static DateTimeOffset? ParseDate(JsonElement element, string name) =>
        OptionalString(element, name) is { } value && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

    private sealed class AccessTokenCache
    {
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public string? AccessToken { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }
}

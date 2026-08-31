using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalGateway : IPayPalGateway
{
    private const string AccessTokenCacheKey = "PayPal.AccessToken";
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly PayPalOptions _options;
    private readonly string _baseUrl;

    public PayPalGateway(HttpClient httpClient, IMemoryCache cache, IOptions<PayPalOptions> options)
    {
        _httpClient = httpClient;
        _cache = cache;
        _options = options.Value;
        _baseUrl = ResolveBaseUrl(_options);
    }

    public async Task<PayPalOrderResult> CreateOrderAsync(int orderId, string paymentReference, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = paymentReference,
                    custom_id = paymentReference,
                    invoice_id = $"eshop-{paymentReference}",
                    amount = Money(amount, currency)
                }
            }
        };
        using var json = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId,
            cancellationToken);
        return new PayPalOrderResult(RequiredString(json.RootElement, "id"),
            RequiredString(json.RootElement, "status"));
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string payPalOrderId,
        PaymentCard? card, string? vaultId, string requestId, CancellationToken cancellationToken)
    {
        object paymentSource = card != null
            ? new { card = CardPayload(card) }
            : new { card = new { vault_id = vaultId } };
        using var json = await SendAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize",
            new { payment_source = paymentSource }, requestId, cancellationToken);

        ThrowIfPayerActionRequired(json.RootElement);
        var authorization = json.RootElement
            .GetProperty("purchase_units")[0]
            .GetProperty("payments")
            .GetProperty("authorizations")[0];
        return ParseAuthorization(authorization);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null,
            cancellationToken);
        return ParseAuthorization(json.RootElement);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId,
        decimal amount, string currency, string requestId, CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = Money(amount, currency) }, requestId, cancellationToken);
        return ParseAuthorization(json.RootElement);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string paymentReference,
        decimal amount, string currency, string requestId, CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new
            {
                amount = Money(amount, currency),
                invoice_id = $"eshop-{paymentReference}",
                final_capture = true
            }, requestId, cancellationToken);
        return ParseCapture(json.RootElement);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId,
        CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null, cancellationToken);
        return ParseCapture(json.RootElement);
    }

    public async Task VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            new { }, requestId, cancellationToken, allowEmpty: true);
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new { amount = Money(amount, currency) }, requestId, cancellationToken);
        var root = json.RootElement;
        return new PayPalRefundResult(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            MoneyValue(root.GetProperty("amount")),
            RequiredString(root.GetProperty("amount"), "currency_code"),
            OptionalDate(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task<PayPalVaultResult> SaveCardAsync(string buyerId, string? payPalCustomerId,
        PaymentCard card, string requestId, CancellationToken cancellationToken)
    {
        var merchantCustomerId = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(buyerId))).ToLowerInvariant();
        object customer = payPalCustomerId == null
            ? new { merchant_customer_id = merchantCustomerId }
            : new { id = payPalCustomerId };
        using var setup = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens",
            new { customer, payment_source = new { card = CardPayload(card) } },
            $"{requestId}-setup", cancellationToken);

        ThrowIfPayerActionRequired(setup.RootElement);
        var setupStatus = RequiredString(setup.RootElement, "status");
        if (setupStatus != "APPROVED")
        {
            throw new PayPalApiException(409, "vault_not_approved",
                $"PayPal did not approve card vaulting (status {setupStatus}).");
        }

        var setupId = RequiredString(setup.RootElement, "id");
        using var token = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens",
            new { payment_source = new { token = new { id = setupId, type = "SETUP_TOKEN" } } },
            $"{requestId}-token", cancellationToken);
        var root = token.RootElement;
        var savedCard = root.GetProperty("payment_source").GetProperty("card");
        var customerId = root.TryGetProperty("customer", out var customerElement)
            ? OptionalString(customerElement, "id")
            : null;
        return new PayPalVaultResult(
            RequiredString(root, "id"), customerId,
            RequiredString(savedCard, "brand"), RequiredString(savedCard, "last_digits"),
            RequiredString(savedCard, "expiry"), OptionalString(savedCard, "name"));
    }

    public async Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await SendAsync(HttpMethod.Delete,
                $"/v3/vault/payment-tokens/{Uri.EscapeDataString(tokenId)}", null, null,
                cancellationToken, allowEmpty: true);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            // A previously deleted remote token is already in the desired state.
        }
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var transactions = new List<PayPalTransaction>();
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            var totalPages = 1;
            do
            {
                var path = "/v1/reporting/transactions?start_date=" +
                           Uri.EscapeDataString(FormatDate(windowStart)) + "&end_date=" +
                           Uri.EscapeDataString(FormatDate(windowEnd)) +
                           $"&fields=transaction_info&page_size=500&page={page}";
                using var json = await SendAsync(HttpMethod.Get, path, null, null, cancellationToken,
                    enforceIsoDates: true);
                var root = json.RootElement;
                if (root.TryGetProperty("transaction_details", out var details))
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        transactions.Add(ParseTransaction(detail.GetProperty("transaction_info")));
                    }
                }

                totalPages = root.TryGetProperty("total_pages", out var total)
                    ? Math.Max(1, total.GetInt32())
                    : 1;
                page++;
            } while (page <= totalPages);

            windowStart = windowEnd;
        }

        return transactions
            .GroupBy(x => new { x.TransactionId, x.EventCode, x.UpdatedAt })
            .Select(x => x.First())
            .ToList();
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken, bool allowEmpty = false,
        bool enforceIsoDates = false)
    {
        using var request = new HttpRequestMessage(method, _baseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            await GetAccessTokenAsync(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (requestId != null)
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (enforceIsoDates)
        {
            request.Headers.TryAddWithoutValidation("PayPal-Enforce-ISO8601-Format", "true");
        }
        if (body != null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ParseError((int)response.StatusCode, responseText);
        }
        if (string.IsNullOrWhiteSpace(responseText))
        {
            if (!allowEmpty)
            {
                throw new PayPalApiException(502, "empty_paypal_response",
                    "PayPal returned an empty response where payment details were required.");
            }
            return JsonDocument.Parse("{}");
        }

        return JsonDocument.Parse(responseText);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<string>(AccessTokenCacheKey, out var cached) && cached != null)
        {
            return cached;
        }

        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue<string>(AccessTokenCacheKey, out cached) && cached != null)
            {
                return cached;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw ParseError((int)response.StatusCode, responseText);
            }

            using var json = JsonDocument.Parse(responseText);
            var token = RequiredString(json.RootElement, "access_token");
            var expiresIn = json.RootElement.TryGetProperty("expires_in", out var expiry)
                ? expiry.GetInt32()
                : 900;
            _cache.Set(AccessTokenCacheKey, token,
                TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60)));
            return token;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private static PayPalApiException ParseError(int statusCode, string responseText)
    {
        try
        {
            using var json = JsonDocument.Parse(responseText);
            var root = json.RootElement;
            var code = OptionalString(root, "name") ?? OptionalString(root, "error") ?? "paypal_error";
            var message = OptionalString(root, "message") ??
                          OptionalString(root, "error_description") ?? "PayPal rejected the request.";
            if (root.TryGetProperty("details", out var details))
            {
                var issues = details.EnumerateArray()
                    .Select(x => OptionalString(x, "description") ?? OptionalString(x, "issue"))
                    .Where(x => x != null);
                var issueText = string.Join(" ", issues!);
                if (issueText.Length > 0) message += " " + issueText;
            }
            return new PayPalApiException(statusCode, code, message,
                OptionalString(root, "debug_id"));
        }
        catch (JsonException)
        {
            return new PayPalApiException(statusCode, "paypal_http_error",
                $"PayPal returned HTTP {statusCode}.");
        }
    }

    private static object CardPayload(PaymentCard card) => new
    {
        number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
        expiry = card.Expiry,
        security_code = card.SecurityCode,
        name = card.Name,
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

    private static object Money(decimal value, string currency) => new
    {
        currency_code = currency.ToUpperInvariant(),
        value = value.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static PayPalAuthorizationResult ParseAuthorization(JsonElement root)
    {
        var amount = root.GetProperty("amount");
        return new PayPalAuthorizationResult(
            RequiredString(root, "id"), RequiredString(root, "status"), MoneyValue(amount),
            RequiredString(amount, "currency_code"),
            OptionalDate(root, "create_time") ?? DateTimeOffset.UtcNow,
            OptionalDate(root, "expiration_time") ?? DateTimeOffset.UtcNow.AddDays(29));
    }

    private static PayPalCaptureResult ParseCapture(JsonElement root)
    {
        var amount = root.GetProperty("amount");
        decimal fee = 0;
        decimal net = MoneyValue(amount);
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            if (breakdown.TryGetProperty("paypal_fee", out var feeElement)) fee = MoneyValue(feeElement);
            if (breakdown.TryGetProperty("net_amount", out var netElement)) net = MoneyValue(netElement);
        }
        return new PayPalCaptureResult(
            RequiredString(root, "id"), RequiredString(root, "status"), MoneyValue(amount),
            RequiredString(amount, "currency_code"), fee, net,
            OptionalDate(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    private static PayPalTransaction ParseTransaction(JsonElement root)
    {
        decimal? amount = null;
        string? currency = null;
        decimal? fee = null;
        if (root.TryGetProperty("transaction_amount", out var amountElement))
        {
            amount = MoneyValue(amountElement);
            currency = OptionalString(amountElement, "currency_code");
        }
        if (root.TryGetProperty("fee_amount", out var feeElement)) fee = MoneyValue(feeElement);
        return new PayPalTransaction(
            RequiredString(root, "transaction_id"), OptionalString(root, "paypal_reference_id"),
            OptionalString(root, "paypal_reference_id_type"),
            OptionalString(root, "transaction_event_code"), OptionalString(root, "transaction_status"),
            amount, currency, fee, OptionalDate(root, "transaction_initiation_date"),
            OptionalDate(root, "transaction_updated_date"), OptionalString(root, "invoice_id"));
    }

    private static void ThrowIfPayerActionRequired(JsonElement root)
    {
        if (!root.TryGetProperty("links", out var links)) return;
        var action = links.EnumerateArray().FirstOrDefault(x =>
            OptionalString(x, "rel") is "payer-action" or "approve");
        if (action.ValueKind != JsonValueKind.Undefined)
        {
            throw new PayPalApiException(409, "payer_action_required",
                "PayPal requires an interactive payer challenge; this headless card flow cannot continue.",
                payerActionRequired: true);
        }
    }

    private static string ResolveBaseUrl(PayPalOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.TrimEnd('/');
        }
        return options.Environment.Equals("Sandbox", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.sandbox.paypal.com"
            : options.Environment.Equals("Live", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.paypal.com"
                : throw new InvalidOperationException("PayPal:Environment must be Sandbox or Live.");
    }

    private static string RequiredString(JsonElement element, string property) =>
        OptionalString(element, property) ??
        throw new PayPalApiException(502, "invalid_paypal_response",
            $"PayPal response omitted required field '{property}'.");

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? OptionalDate(JsonElement element, string property) =>
        OptionalString(element, property) is { } value &&
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static decimal MoneyValue(JsonElement element) =>
        decimal.Parse(RequiredString(element, "value"), NumberStyles.Number,
            CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

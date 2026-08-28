using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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

    public string Currency
    {
        get
        {
            ValidateOptions();
            return _options.Currency.ToUpperInvariant();
        }
    }

    public async Task<PayPalAuthorization> AuthorizeAsync(int orderId, string paymentReference, decimal amount, CardData? card,
        string? paymentTokenId, string requestId, CancellationToken cancellationToken)
    {
        if ((card is null) == string.IsNullOrWhiteSpace(paymentTokenId))
        {
            throw new ArgumentException("Supply exactly one card or saved payment token.");
        }

        object paymentSource = card is not null
            ? new
            {
                card = new
                {
                    number = card.Number,
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    name = card.Name,
                    billing_address = Address(card.BillingAddress)
                }
            }
            : new { token = new { id = paymentTokenId, type = "PAYMENT_METHOD_TOKEN" } };

        var payload = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = $"eshop-{paymentReference}",
                    custom_id = orderId.ToString(CultureInfo.InvariantCulture),
                    invoice_id = $"eshop-{paymentReference}",
                    amount = Money(amount)
                }
            },
            payment_source = paymentSource
        };

        using var document = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", payload,
            requestId, cancellationToken);
        EnsureNoPayerAction(document.RootElement);

        var authorization = TryParseAuthorization(document.RootElement);
        if (authorization is not null)
        {
            return authorization;
        }

        var payPalOrderId = RequiredString(document.RootElement, "id");
        using var authorized = await SendJsonAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize", new { },
            requestId + "-execute", cancellationToken);
        EnsureNoPayerAction(authorized.RootElement);
        return TryParseAuthorization(authorized.RootElement)
            ?? throw new PayPalException(HttpStatusCode.BadGateway, "MISSING_AUTHORIZATION",
                "PayPal did not return an authorization for the order.");
    }

    public async Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount,
        string requestId, CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = Money(amount) }, requestId, cancellationToken);
        var root = document.RootElement;
        var money = root.GetProperty("amount");
        return new PayPalAuthorization(
            string.Empty,
            string.Empty,
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            DecimalValue(money),
            RequiredString(money, "currency_code"),
            DateValue(root, "create_time", DateTimeOffset.UtcNow),
            DateValue(root, "expiration_time", DateTimeOffset.UtcNow.AddDays(3)));
    }

    public async Task<PayPalCapture> CaptureAsync(string authorizationId, string paymentReference, decimal amount,
        string requestId, CancellationToken cancellationToken)
    {
        var payload = new
        {
            amount = Money(amount),
            invoice_id = $"eshop-{paymentReference}",
            final_capture = true
        };
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            payload, requestId, cancellationToken);
        var root = document.RootElement;
        var status = RequiredString(root, "status");
        if (!root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            return new PayPalCapture(RequiredString(root, "id"), status, amount, Currency, 0m, 0m);
        }
        var gross = breakdown.GetProperty("gross_amount");
        return new PayPalCapture(
            RequiredString(root, "id"),
            status,
            DecimalValue(gross),
            RequiredString(gross, "currency_code"),
            DecimalValue(breakdown.GetProperty("paypal_fee")),
            DecimalValue(breakdown.GetProperty("net_amount")));
    }

    public async Task<string> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            new { }, requestId, cancellationToken, allowEmptyBody: true);
        return document.RootElement.ValueKind == JsonValueKind.Object &&
               document.RootElement.TryGetProperty("status", out var status)
            ? status.GetString() ?? "VOIDED"
            : "VOIDED";
    }

    public async Task<PayPalRefund> RefundAsync(string captureId, decimal amount, string requestId,
        CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new { amount = Money(amount) }, requestId, cancellationToken);
        var root = document.RootElement;
        var money = root.GetProperty("amount");
        return new PayPalRefund(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            DecimalValue(money),
            RequiredString(money, "currency_code"),
            DateValue(root, "create_time", DateTimeOffset.UtcNow));
    }

    public async Task<PayPalVaultToken> SaveCardAsync(CardData card, string? customerId,
        string requestId, CancellationToken cancellationToken)
    {
        object payload = string.IsNullOrWhiteSpace(customerId)
            ? new
            {
                payment_source = new
                {
                    card = new
                    {
                        number = card.Number,
                        expiry = card.Expiry,
                        name = card.Name,
                        billing_address = Address(card.BillingAddress)
                    }
                }
            }
            : new
            {
                customer = new { id = customerId },
                payment_source = new
                {
                    card = new
                    {
                        number = card.Number,
                        expiry = card.Expiry,
                        name = card.Name,
                        billing_address = Address(card.BillingAddress)
                    }
                }
            };

        using var setup = await SendJsonAsync(HttpMethod.Post, "/v3/vault/setup-tokens", payload,
            requestId + "-setup", cancellationToken);
        EnsureNoPayerAction(setup.RootElement);
        if (!string.Equals(RequiredString(setup.RootElement, "status"), "APPROVED",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalException(HttpStatusCode.UnprocessableEntity, "SETUP_TOKEN_NOT_APPROVED",
                "PayPal did not approve the card for vaulting.");
        }

        var setupTokenId = RequiredString(setup.RootElement, "id");
        using var paymentToken = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens",
            new { payment_source = new { token = new { id = setupTokenId, type = "SETUP_TOKEN" } } },
            requestId + "-token", cancellationToken);
        var root = paymentToken.RootElement;
        var savedCard = root.GetProperty("payment_source").GetProperty("card");
        return new PayPalVaultToken(
            RequiredString(root, "id"),
            RequiredString(root.GetProperty("customer"), "id"),
            RequiredString(savedCard, "brand"),
            RequiredString(savedCard, "last_digits"),
            RequiredString(savedCard, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}", null, null,
            cancellationToken, allowEmptyBody: true);
    }

    public async Task<IReadOnlyCollection<PayPalTransaction>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var transactions = new List<PayPalTransaction>();
        var seen = new HashSet<PayPalTransaction>();
        var chunkStart = from;
        while (chunkStart < to)
        {
            var chunkEnd = chunkStart.AddDays(30) < to ? chunkStart.AddDays(30) : to;
            var page = 1;
            var totalPages = 1;
            do
            {
                var path = "/v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(IsoDate(chunkStart))}" +
                    $"&end_date={Uri.EscapeDataString(IsoDate(chunkEnd))}" +
                    "&fields=transaction_info&balance_affecting_records_only=N&page_size=500" +
                    $"&page={page}";
                using var document = await SendJsonAsync(HttpMethod.Get, path, null, null,
                    cancellationToken);
                var root = document.RootElement;
                totalPages = root.TryGetProperty("total_pages", out var pages) ? pages.GetInt32() : 1;
                if (root.TryGetProperty("transaction_details", out var details))
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        var info = detail.GetProperty("transaction_info");
                        var amount = info.GetProperty("transaction_amount");
                        var transaction = new PayPalTransaction(
                            RequiredString(info, "transaction_id"),
                            OptionalString(info, "paypal_reference_id"),
                            OptionalString(info, "paypal_reference_id_type"),
                            OptionalString(info, "transaction_event_code") ?? string.Empty,
                            OptionalString(info, "transaction_status") ?? string.Empty,
                            DateValue(info, "transaction_initiation_date", from),
                            DecimalValue(amount),
                            info.TryGetProperty("fee_amount", out var fee) ? DecimalValue(fee) : 0m,
                            RequiredString(amount, "currency_code"));
                        if (seen.Add(transaction)) transactions.Add(transaction);
                    }
                }
                page++;
            } while (page <= totalPages);

            if (chunkEnd == to) break;
            chunkStart = chunkEnd;
        }

        return transactions;
    }

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken, bool allowEmptyBody = false)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ParseError(response.StatusCode, json);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            if (!allowEmptyBody)
            {
                throw new PayPalException(HttpStatusCode.BadGateway, "EMPTY_RESPONSE",
                    "PayPal returned an empty response.");
            }
            return JsonDocument.Parse("null");
        }
        return JsonDocument.Parse(json);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();
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

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw ParseError(response.StatusCode, json);
            using var document = JsonDocument.Parse(json);
            _accessToken = RequiredString(document.RootElement, "access_token");
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expires)
                ? expires.GetInt32()
                : 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private PayPalException ParseError(HttpStatusCode statusCode, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var code = OptionalString(root, "name") ?? "PAYPAL_ERROR";
            var debugId = OptionalString(root, "debug_id");
            var message = OptionalString(root, "message") ?? "PayPal rejected the operation.";
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                var first = details.EnumerateArray().FirstOrDefault();
                var issue = OptionalString(first, "issue");
                var description = OptionalString(first, "description");
                if (!string.IsNullOrWhiteSpace(issue)) code = issue;
                if (!string.IsNullOrWhiteSpace(description)) message = description;
            }
            if (string.Equals(code, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            {
                return new PayPalPayerActionRequiredException(
                    "PayPal requires an interactive cardholder challenge; this headless API cannot complete that payment.",
                    debugId);
            }
            return new PayPalException(statusCode, code, message, debugId);
        }
        catch (JsonException)
        {
            return new PayPalException(statusCode, "PAYPAL_ERROR", "PayPal rejected the operation.");
        }
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new InvalidOperationException("PayPal:ClientId and PayPal:ClientSecret must be configured.");
        if (string.IsNullOrWhiteSpace(_options.Currency) || _options.Currency.Length != 3)
            throw new InvalidOperationException("PayPal:Currency must be a three-letter ISO-4217 code.");
        if (string.IsNullOrWhiteSpace(_options.Environment) && string.IsNullOrWhiteSpace(_options.BaseUrl))
            throw new InvalidOperationException("PayPal:Environment must be configured when PayPal:BaseUrl is not set.");
    }

    private Uri BuildUri(string path)
    {
        var baseUrl = !string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? _options.BaseUrl
            : _options.Environment.Equals("sandbox", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.sandbox.paypal.com"
                : _options.Environment.Equals("live", StringComparison.OrdinalIgnoreCase) ||
                  _options.Environment.Equals("production", StringComparison.OrdinalIgnoreCase)
                    ? "https://api-m.paypal.com"
                    : throw new InvalidOperationException("PayPal:Environment must be 'sandbox' or 'live'.");
        return new Uri(baseUrl!.TrimEnd('/') + path, UriKind.Absolute);
    }

    private object Money(decimal amount) => new
    {
        currency_code = Currency,
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static object Address(BillingAddressData address) => new
    {
        address_line_1 = address.AddressLine1,
        address_line_2 = address.AddressLine2,
        admin_area_2 = address.City,
        admin_area_1 = address.State,
        postal_code = address.PostalCode,
        country_code = address.CountryCode.ToUpperInvariant()
    };

    private static PayPalAuthorization? TryParseAuthorization(JsonElement root)
    {
        if (!root.TryGetProperty("purchase_units", out var purchaseUnits)) return null;
        foreach (var purchaseUnit in purchaseUnits.EnumerateArray())
        {
            if (!purchaseUnit.TryGetProperty("payments", out var payments) ||
                !payments.TryGetProperty("authorizations", out var authorizations)) continue;
            var authorization = authorizations.EnumerateArray().FirstOrDefault();
            if (authorization.ValueKind != JsonValueKind.Object) continue;
            var money = authorization.GetProperty("amount");
            return new PayPalAuthorization(
                RequiredString(root, "id"),
                RequiredString(root, "status"),
                RequiredString(authorization, "id"),
                RequiredString(authorization, "status"),
                DecimalValue(money),
                RequiredString(money, "currency_code"),
                DateValue(authorization, "create_time", DateTimeOffset.UtcNow),
                DateValue(authorization, "expiration_time", DateTimeOffset.UtcNow.AddDays(29)));
        }
        return null;
    }

    private static void EnsureNoPayerAction(JsonElement root)
    {
        var status = OptionalString(root, "status");
        var hasPayerAction = root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array &&
            links.EnumerateArray().Any(link =>
                string.Equals(OptionalString(link, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(OptionalString(link, "rel"), "approve", StringComparison.OrdinalIgnoreCase));
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) || hasPayerAction)
        {
            throw new PayPalPayerActionRequiredException(
                "PayPal requires an interactive cardholder challenge; this headless API cannot complete that payment.");
        }
    }

    private static string RequiredString(JsonElement element, string property) =>
        OptionalString(element, property) ?? throw new JsonException($"PayPal response omitted '{property}'.");

    private static string? OptionalString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static decimal DecimalValue(JsonElement money) =>
        decimal.Parse(RequiredString(money, "value"), NumberStyles.Number, CultureInfo.InvariantCulture);

    private static DateTimeOffset DateValue(JsonElement element, string property, DateTimeOffset fallback) =>
        DateTimeOffset.TryParse(OptionalString(element, property), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var value) ? value : fallback;

    private static string IsoDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

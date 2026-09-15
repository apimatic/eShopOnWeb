using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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

    public async Task<VaultedCardResult> VaultCardAsync(string merchantCustomerId, string? paypalCustomerId, CardDetails card,
        string requestId, CancellationToken cancellationToken)
    {
        ValidateCard(card);
        var setupPayload = new Dictionary<string, object?>
        {
            ["customer"] = paypalCustomerId is null
                ? new { merchant_customer_id = merchantCustomerId }
                : new { id = paypalCustomerId },
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = CardPayload(card)
            }
        };

        using var setup = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupPayload,
            requestId, cancellationToken: cancellationToken);
        var setupRoot = setup.RootElement;
        var setupStatus = RequiredString(setupRoot, "status");
        if (setupStatus == "PAYER_ACTION_REQUIRED")
            throw new PayPalChallengeRequiredException("PayPal requires browser approval to save this card; no payment token was created.");
        if (setupStatus != "APPROVED")
            throw new PayPalApiException(422, "VAULT_SETUP_NOT_APPROVED",
                $"The setup token status is {setupStatus}", null, Array.Empty<string>());

        var setupTokenId = RequiredString(setupRoot, "id");
        var tokenPayload = new
        {
            payment_source = new
            {
                token = new { id = setupTokenId, type = "SETUP_TOKEN" }
            }
        };

        using var token = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenPayload,
            requestId + "-token", cancellationToken: cancellationToken);
        var root = token.RootElement;
        var cardResult = root.GetProperty("payment_source").GetProperty("card");
        return new VaultedCardResult(
            RequiredString(root, "id"),
            RequiredString(root.GetProperty("customer"), "id"),
            RequiredString(cardResult, "brand"),
            RequiredString(cardResult, "last_digits"),
            RequiredString(cardResult, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await SendAsync(HttpMethod.Delete,
                $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}", null, null,
                cancellationToken: cancellationToken);
        }
        catch (PayPalApiException exception) when (exception.StatusCode == (int)HttpStatusCode.NotFound)
        {
            // A retry after an ambiguous successful delete has the desired final state.
        }
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardAsync(string orderReference, decimal amount, string currency,
        CardDetails card, string requestId, CancellationToken cancellationToken)
    {
        ValidateCard(card);
        return AuthorizeAsync(orderReference, amount, currency,
            new Dictionary<string, object?> { ["card"] = CardPayload(card) }, requestId, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeSavedCardAsync(string orderReference, decimal amount, string currency,
        string vaultId, string requestId, CancellationToken cancellationToken) =>
        AuthorizeAsync(orderReference, amount, currency,
            new Dictionary<string, object?> { ["card"] = new { vault_id = vaultId } }, requestId, cancellationToken);

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null,
            cancellationToken: cancellationToken);
        return ParseAuthorization(document.RootElement, null);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        var payload = new { amount = Money(amount, currency) };
        using var document = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            payload, requestId, "return=representation", cancellationToken);
        return ParseAuthorization(document.RootElement, null);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        var payload = new
        {
            amount = Money(amount, currency),
            final_capture = true
        };
        using var document = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            payload, requestId, "return=representation", cancellationToken);
        return ParseCapture(document.RootElement);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null,
            cancellationToken: cancellationToken);
        return ParseCapture(document.RootElement);
    }

    private static PayPalCaptureResult ParseCapture(JsonElement root)
    {
        var status = RequiredString(root, "status");
        decimal? fee = null;
        decimal? net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = OptionalMoneyValue(breakdown, "paypal_fee");
            net = OptionalMoneyValue(breakdown, "net_amount");
        }
        if (status == "COMPLETED" && (fee is null || net is null))
            throw new PayPalApiException(502, "INVALID_PAYPAL_RESPONSE",
                "PayPal omitted fee or net proceeds from a completed capture", null, Array.Empty<string>());
        return new PayPalCaptureResult(
            RequiredString(root, "id"),
            status,
            MoneyValue(root.GetProperty("amount")),
            RequiredString(root.GetProperty("amount"), "currency_code"),
            fee,
            net,
            OptionalDate(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task<string> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await SendAsync(HttpMethod.Post,
                $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
                new { }, requestId, cancellationToken: cancellationToken);
            return "VOIDED";
        }
        catch (PayPalApiException exception) when (exception.StatusCode == 422)
        {
            var current = await GetAuthorizationAsync(authorizationId, cancellationToken);
            if (current.Status == "VOIDED")
                return current.Status;
            throw;
        }
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new { amount = Money(amount, currency) }, requestId, "return=representation", cancellationToken);
        var root = document.RootElement;
        return new PayPalRefundResult(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            MoneyValue(root.GetProperty("amount")),
            RequiredString(root.GetProperty("amount"), "currency_code"),
            OptionalDate(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyCollection<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<PayPalTransaction>();
        var windowStart = from;
        while (windowStart <= to)
        {
            var maxWindowEnd = windowStart.AddDays(31).AddTicks(-1);
            var windowEnd = maxWindowEnd < to ? maxWindowEnd : to;
            await ReadTransactionWindowAsync(windowStart, windowEnd, results, cancellationToken);
            if (windowEnd == to)
                break;
            windowStart = windowEnd.AddTicks(1);
        }
        return results;
    }

    private async Task ReadTransactionWindowAsync(DateTimeOffset from, DateTimeOffset to,
        ICollection<PayPalTransaction> destination, CancellationToken cancellationToken)
    {
        var page = 1;
        var totalPages = 1;
        do
        {
            var path = "/v1/reporting/transactions" +
                       $"?start_date={Uri.EscapeDataString(PayPalDate(from))}" +
                       $"&end_date={Uri.EscapeDataString(PayPalDate(to))}" +
                       $"&fields=all&balance_affecting_records_only=N&page_size=500&page={page}";
            JsonDocument document;
            try
            {
                document = await SendAsync(HttpMethod.Get, path, null, null,
                    additionalHeaders: new Dictionary<string, string> { ["PayPal-Enforce-ISO8601-Format"] = "true" },
                    cancellationToken: cancellationToken);
            }
            catch (PayPalApiException exception) when (
                exception.StatusCode == 404 && exception.Name == "INVALID_REQUEST" &&
                exception.ProcessorMessage.Contains("Data for the given start date is not available", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            using (document)
            {
            var root = document.RootElement;
            totalPages = root.TryGetProperty("total_pages", out var pages) ? pages.GetInt32() : 0;
            if (root.TryGetProperty("transaction_details", out var details))
            {
                foreach (var detail in details.EnumerateArray())
                {
                    var info = detail.GetProperty("transaction_info");
                    var amount = OptionalMoneyValue(info, "transaction_amount");
                    var fee = OptionalMoneyValue(info, "fee_amount");
                    string? currency = null;
                    if (info.TryGetProperty("transaction_amount", out var amountElement))
                        currency = OptionalString(amountElement, "currency_code");
                    destination.Add(new PayPalTransaction(
                        RequiredString(info, "transaction_id"),
                        OptionalString(info, "paypal_reference_id"),
                        OptionalString(info, "paypal_reference_id_type"),
                        OptionalString(info, "transaction_event_code") ?? string.Empty,
                        OptionalString(info, "transaction_status") ?? string.Empty,
                        OptionalDate(info, "transaction_initiation_date") ?? from,
                        OptionalDate(info, "transaction_updated_date"),
                        amount,
                        fee,
                        currency));
                }
            }
            page++;
            }
        } while (page <= totalPages);
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(string orderReference, decimal amount, string currency,
        object paymentSource, string requestId, CancellationToken cancellationToken)
    {
        var payload = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = $"ESHOP-{orderReference}",
                    custom_id = $"ESHOP-{orderReference}",
                    invoice_id = $"ESHOP-{orderReference}",
                    amount = Money(amount, currency)
                }
            },
            payment_source = paymentSource
        };
        using var document = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", payload,
            requestId, "return=representation", cancellationToken);
        var root = document.RootElement;
        var status = RequiredString(root, "status");
        if (status == "PAYER_ACTION_REQUIRED")
            throw new PayPalChallengeRequiredException("PayPal requires browser approval for this card authorization; no authorization was accepted by eShop.");
        if (!root.TryGetProperty("purchase_units", out var units) ||
            !units[0].TryGetProperty("payments", out var payments) ||
            !payments.TryGetProperty("authorizations", out var authorizations) ||
            authorizations.GetArrayLength() == 0)
        {
            throw new PayPalApiException(502, "AUTHORIZATION_MISSING",
                $"PayPal returned order status {status} without an authorization", null, Array.Empty<string>());
        }
        return ParseAuthorization(authorizations[0], RequiredString(root, "id"));
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body,
        string? requestId, string? prefer = null, CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? additionalHeaders = null)
    {
        _options.EnsureConfigured();
        var json = body is null ? null : JsonSerializer.Serialize(body, JsonOptions);
        var response = await SendWithTokenAsync(method, path, json, requestId, prefer, additionalHeaders,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            _accessToken = null;
            response = await SendWithTokenAsync(method, path, json, requestId, prefer, additionalHeaders,
                cancellationToken);
        }

        using (response)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw ParseError((int)response.StatusCode, responseText);
            return string.IsNullOrWhiteSpace(responseText)
                ? JsonDocument.Parse("{}")
                : JsonDocument.Parse(responseText);
        }
    }

    private async Task<HttpResponseMessage> SendWithTokenAsync(HttpMethod method, string path, string? json,
        string? requestId, string? prefer, IReadOnlyDictionary<string, string>? additionalHeaders,
        CancellationToken cancellationToken)
    {
        var accessToken = await GetAccessTokenAsync(cancellationToken);
        var request = new HttpRequestMessage(method, BuildUrl(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(requestId))
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        if (!string.IsNullOrWhiteSpace(prefer))
            request.Headers.TryAddWithoutValidation("Prefer", prefer);
        if (additionalHeaders is not null)
            foreach (var header in additionalHeaders)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _httpClientFactory.CreateClient("PayPal").SendAsync(request, cancellationToken);
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

            _options.EnsureConfigured();
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl("/v1/oauth2/token"));
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClientFactory.CreateClient("PayPal").SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw ParseError((int)response.StatusCode, responseText);
            using var document = JsonDocument.Parse(responseText);
            _accessToken = RequiredString(document.RootElement, "access_token");
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiry)
                ? expiry.GetInt32()
                : 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private string BuildUrl(string path) => $"{_options.ResolveBaseUrl().TrimEnd('/')}/{path.TrimStart('/')}";

    private static string PayPalDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static object Money(decimal value, string currency) => new
    {
        currency_code = currency.ToUpperInvariant(),
        value = value.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static object CardPayload(CardDetails card) => new
    {
        number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
        expiry = card.Expiry,
        security_code = card.SecurityCode,
        name = card.Name,
        billing_address = new
        {
            address_line_1 = card.BillingAddress.AddressLine1,
            address_line_2 = card.BillingAddress.AddressLine2,
            admin_area_1 = card.BillingAddress.AdminArea1,
            admin_area_2 = card.BillingAddress.AdminArea2,
            postal_code = card.BillingAddress.PostalCode,
            country_code = card.BillingAddress.CountryCode.ToUpperInvariant()
        }
    };

    private static void ValidateCard(CardDetails card)
    {
        var number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (number.Length is < 13 or > 19 || number.Any(character => !char.IsDigit(character)))
            throw new PaymentValidationException("Card number must contain 13 to 19 digits.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(card.Expiry, "^[0-9]{4}-(0[1-9]|1[0-2])$"))
            throw new PaymentValidationException("Card expiry must use YYYY-MM format.");
        if (card.SecurityCode.Length is < 3 or > 4 || card.SecurityCode.Any(character => !char.IsDigit(character)))
            throw new PaymentValidationException("Card security code must contain three or four digits.");
        if (string.IsNullOrWhiteSpace(card.Name) || string.IsNullOrWhiteSpace(card.BillingAddress.AddressLine1) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.AdminArea2) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.PostalCode) || card.BillingAddress.CountryCode.Length != 2)
            throw new PaymentValidationException("Cardholder name and a complete billing address are required.");
    }

    private static PayPalAuthorizationResult ParseAuthorization(JsonElement root, string? orderId)
    {
        var amount = root.GetProperty("amount");
        var createdAt = OptionalDate(root, "create_time") ?? DateTimeOffset.UtcNow;
        return new PayPalAuthorizationResult(
            orderId ?? string.Empty,
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            MoneyValue(amount),
            RequiredString(amount, "currency_code"),
            createdAt,
            OptionalDate(root, "expiration_time") ?? createdAt.AddDays(29));
    }

    private static PayPalApiException ParseError(int statusCode, string responseText)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            var issues = new List<string>();
            if (root.TryGetProperty("details", out var details))
            {
                foreach (var detail in details.EnumerateArray())
                {
                    var issue = OptionalString(detail, "issue");
                    var description = OptionalString(detail, "description");
                    if (!string.IsNullOrWhiteSpace(issue) || !string.IsNullOrWhiteSpace(description))
                        issues.Add($"{issue}: {description}".Trim(' ', ':'));
                }
            }
            return new PayPalApiException(statusCode,
                OptionalString(root, "name") ?? "PAYPAL_ERROR",
                OptionalString(root, "message") ?? "The payment processor rejected the request",
                OptionalString(root, "debug_id"), issues);
        }
        catch (JsonException)
        {
            return new PayPalApiException(statusCode, "PAYPAL_ERROR",
                "The payment processor returned an unreadable error", null, Array.Empty<string>());
        }
    }

    private static string RequiredString(JsonElement element, string property) =>
        OptionalString(element, property) ?? throw new PayPalApiException(502, "INVALID_PAYPAL_RESPONSE",
            $"PayPal omitted required field {property}", null, Array.Empty<string>());

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? OptionalDate(JsonElement element, string property) =>
        OptionalString(element, property) is { } text &&
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;

    private static decimal MoneyValue(JsonElement money) =>
        decimal.Parse(RequiredString(money, "value"), NumberStyles.Number, CultureInfo.InvariantCulture);

    private static decimal? OptionalMoneyValue(JsonElement element, string property) =>
        element.TryGetProperty(property, out var money) && OptionalString(money, "value") is { } value &&
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}

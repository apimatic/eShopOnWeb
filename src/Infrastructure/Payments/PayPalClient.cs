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
using BlazorShared;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly string _applicationBaseUrl;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options,
        IOptions<BaseUrlConfiguration> baseUrlOptions)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _applicationBaseUrl = baseUrlOptions.Value.ApiBase?.TrimEnd('/') ?? string.Empty;
    }

    public string Currency => _options.Currency.Trim().ToUpperInvariant();

    public async Task<string> CreateOrderAsync(decimal amount, string paymentReference,
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
                    amount = Money(amount)
                }
            }
        };

        using var document = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body,
            requestId, cancellationToken);
        return RequiredString(document.RootElement, "id");
    }

    public async Task<PayPalAuthorization> AuthorizeOrderAsync(string payPalOrderId,
        CardInput? card, string? vaultId, string requestId, CancellationToken cancellationToken)
    {
        JsonObject cardSource;
        if (card is not null)
        {
            cardSource = CardJson(card, includeExperience: false);
        }
        else if (!string.IsNullOrWhiteSpace(vaultId))
        {
            cardSource = new JsonObject
            {
                ["vault_id"] = vaultId,
                ["stored_credential"] = new JsonObject
                {
                    ["payment_initiator"] = "CUSTOMER",
                    ["payment_type"] = "ONE_TIME",
                    ["usage"] = "SUBSEQUENT"
                }
            };
        }
        else
        {
            throw new PaymentValidationException("A card or saved payment method is required.");
        }

        var body = new JsonObject
        {
            ["payment_source"] = new JsonObject { ["card"] = cardSource }
        };

        using var document = await SendAsync(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize", body,
            requestId, cancellationToken);

        var root = document.RootElement;
        if (NeedsPayerAction(root))
        {
            throw new PayerActionRequiredException();
        }

        var authorization = FirstPayment(root, "authorizations");
        return ParseAuthorization(authorization, payPalOrderId,
            OptionalString(root, "status") ?? "UNKNOWN");
    }

    public async Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId,
        string payPalOrderId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null,
            null, cancellationToken);
        return ParseAuthorization(document.RootElement, payPalOrderId, "UNKNOWN");
    }

    public async Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId,
        string payPalOrderId, decimal amount, string requestId, CancellationToken cancellationToken)
    {
        var body = new { amount = Money(amount) };
        using var document = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            body, requestId, cancellationToken);
        return ParseAuthorization(document.RootElement, payPalOrderId, "UNKNOWN");
    }

    public async Task<PayPalCapture> CaptureAsync(string authorizationId, decimal amount,
        string paymentReference, string requestId, CancellationToken cancellationToken)
    {
        var body = new
        {
            amount = Money(amount),
            invoice_id = $"eshop-{paymentReference}",
            final_capture = true
        };
        using var document = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            body, requestId, cancellationToken);
        return ParseCapture(document.RootElement);
    }

    public async Task<PayPalCapture> GetCaptureAsync(string captureId,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null,
            cancellationToken);
        return ParseCapture(document.RootElement);
    }

    public async Task VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            null, requestId, cancellationToken, allowEmptyResponse: true);
    }

    public async Task<PayPalRefund> RefundAsync(string captureId, decimal amount,
        string paymentReference, string requestId, CancellationToken cancellationToken)
    {
        var body = new
        {
            amount = Money(amount),
            custom_id = paymentReference,
            invoice_id = $"eshop-{paymentReference}"
        };
        using var document = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund", body,
            requestId, cancellationToken);
        return ParseRefund(document.RootElement);
    }

    public async Task<PayPalRefund> GetRefundAsync(string refundId,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get,
            $"/v2/payments/refunds/{Uri.EscapeDataString(refundId)}", null, null,
            cancellationToken);
        return ParseRefund(document.RootElement);
    }

    public async Task<PayPalSavedCard> SaveCardAsync(string merchantCustomerId, CardInput card,
        string requestId, CancellationToken cancellationToken)
    {
        var setupBody = new JsonObject
        {
            ["customer"] = new JsonObject { ["merchant_customer_id"] = merchantCustomerId },
            ["payment_source"] = new JsonObject
            {
                ["card"] = CardJson(card, includeExperience: true)
            }
        };

        using var setup = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody,
            $"setup-{requestId}", cancellationToken);
        var setupRoot = setup.RootElement;
        if (NeedsPayerAction(setupRoot))
        {
            throw new PayerActionRequiredException();
        }

        var setupStatus = OptionalString(setupRoot, "status");
        if (!string.Equals(setupStatus, "APPROVED", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(setupStatus, "VAULTED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentProcessorException(
                $"PayPal did not approve the card for vaulting (status: {setupStatus ?? "UNKNOWN"}).");
        }

        var setupId = RequiredString(setupRoot, "id");
        var customerId = OptionalNestedString(setupRoot, "customer", "id");
        var paymentTokenBody = new JsonObject
        {
            ["payment_source"] = new JsonObject
            {
                ["token"] = new JsonObject
                {
                    ["id"] = setupId,
                    ["type"] = "SETUP_TOKEN"
                }
            }
        };
        using var token = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens",
            paymentTokenBody, $"token-{requestId}", cancellationToken);
        var tokenRoot = token.RootElement;
        var cardElement = NestedRequired(tokenRoot, "payment_source", "card");

        return new PayPalSavedCard(
            RequiredString(tokenRoot, "id"),
            OptionalNestedString(tokenRoot, "customer", "id") ?? customerId,
            RequiredString(cardElement, "brand"),
            RequiredString(cardElement, "last_digits"),
            RequiredString(cardElement, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await SendAsync(HttpMethod.Delete,
                $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}", null, null,
                cancellationToken, allowEmptyResponse: true);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // The desired external state has already been reached.
        }
    }

    public async Task<PayPalTransactionPage> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, int page, CancellationToken cancellationToken)
    {
        var query = string.Join("&", new[]
        {
            $"start_date={Uri.EscapeDataString(PayPalDate(from))}",
            $"end_date={Uri.EscapeDataString(PayPalDate(to))}",
            "fields=transaction_info",
            "balance_affecting_records_only=N",
            "page_size=500",
            $"page={page.ToString(CultureInfo.InvariantCulture)}"
        });

        using var document = await SendAsync(HttpMethod.Get,
            $"/v1/reporting/transactions?{query}", null, null, cancellationToken);
        var root = document.RootElement;
        var transactions = new List<PayPalTransaction>();
        if (root.TryGetProperty("transaction_details", out var details) &&
            details.ValueKind == JsonValueKind.Array)
        {
            foreach (var detail in details.EnumerateArray())
            {
                if (!detail.TryGetProperty("transaction_info", out var info))
                {
                    continue;
                }

                transactions.Add(new PayPalTransaction(
                    RequiredString(info, "transaction_id"),
                    OptionalString(info, "transaction_event_code"),
                    OptionalString(info, "transaction_status"),
                    OptionalDate(info, "transaction_initiation_date"),
                    OptionalMoney(info, "transaction_amount"),
                    OptionalNestedString(info, "transaction_amount", "currency_code"),
                    OptionalMoney(info, "fee_amount"),
                    OptionalString(info, "invoice_id")));
            }
        }

        return new PayPalTransactionPage(
            transactions,
            OptionalInt(root, "page") ?? page,
            Math.Max(1, OptionalInt(root, "total_pages") ?? 1));
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken, bool allowEmptyResponse = false)
    {
        _options.EnsureConfigured();
        var retries = 0;
        while (true)
        {
            using var request = new HttpRequestMessage(method, _options.GetBaseUrl() + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                await GetAccessTokenAsync(cancellationToken));
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (!string.IsNullOrWhiteSpace(requestId))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }
            if (body is not null)
            {
                var json = body is JsonNode node
                    ? node.ToJsonString(JsonOptions)
                    : JsonSerializer.Serialize(body, JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(responseBody))
                {
                    if (!allowEmptyResponse)
                    {
                        throw new PaymentProcessorException("PayPal returned an empty response.");
                    }
                    return JsonDocument.Parse("{}");
                }
                return JsonDocument.Parse(responseBody);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && retries == 0)
            {
                _accessToken = null;
                retries++;
                continue;
            }

            if ((response.StatusCode == HttpStatusCode.TooManyRequests ||
                 (int)response.StatusCode >= 500) && retries < 2)
            {
                retries++;
                await Task.Delay(TimeSpan.FromMilliseconds(200 * retries), cancellationToken);
                continue;
            }

            throw CreateApiException(response.StatusCode, responseBody);
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
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

            using var request = new HttpRequestMessage(HttpMethod.Post,
                _options.GetBaseUrl() + "/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response.StatusCode, responseBody);
            }

            using var document = JsonDocument.Parse(responseBody);
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

    private object Money(decimal amount) => new
    {
        currency_code = Currency,
        value = amount.ToString("F2", CultureInfo.InvariantCulture)
    };

    private JsonObject CardJson(CardInput card, bool includeExperience)
    {
        var result = new JsonObject
        {
            ["name"] = card.Name,
            ["number"] = NormalizeCardNumber(card.Number),
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["billing_address"] = new JsonObject
            {
                ["address_line_1"] = card.BillingAddress.AddressLine1,
                ["address_line_2"] = card.BillingAddress.AddressLine2,
                ["admin_area_2"] = card.BillingAddress.City,
                ["admin_area_1"] = card.BillingAddress.State,
                ["postal_code"] = card.BillingAddress.PostalCode,
                ["country_code"] = card.BillingAddress.CountryCode.ToUpperInvariant()
            }
        };
        if (includeExperience)
        {
            if (!Uri.TryCreate(_applicationBaseUrl, UriKind.Absolute, out _))
            {
                throw new PaymentValidationException(
                    "baseUrls:apiBase must be an absolute URL to save a card.");
            }
            result["experience_context"] = new JsonObject
            {
                ["brand_name"] = "eShopOnWeb",
                ["locale"] = "en-US",
                ["return_url"] = _applicationBaseUrl + "/payment-methods/paypal-return",
                ["cancel_url"] = _applicationBaseUrl + "/payment-methods/paypal-cancel"
            };
        }
        return result;
    }

    internal static string NormalizeCardNumber(string number) =>
        new(number.Where(char.IsDigit).ToArray());

    private static string PayPalDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static PayPalAuthorization ParseAuthorization(JsonElement element,
        string payPalOrderId, string orderStatus)
    {
        var amount = RequiredMoney(element, "amount");
        return new PayPalAuthorization(
            payPalOrderId,
            orderStatus,
            RequiredString(element, "id"),
            RequiredString(element, "status"),
            amount.Amount,
            amount.Currency,
            OptionalDate(element, "create_time"),
            OptionalDate(element, "expiration_time"));
    }

    private static PayPalCapture ParseCapture(JsonElement element)
    {
        var amount = RequiredMoney(element, "amount");
        decimal? fee = null;
        decimal? net = null;
        if (element.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = OptionalMoney(breakdown, "paypal_fee");
            net = OptionalMoney(breakdown, "net_amount");
        }

        return new PayPalCapture(
            RequiredString(element, "id"),
            RequiredString(element, "status"),
            amount.Amount,
            amount.Currency,
            fee,
            net,
            OptionalDate(element, "create_time"));
    }

    private static PayPalRefund ParseRefund(JsonElement element)
    {
        var amount = RequiredMoney(element, "amount");
        return new PayPalRefund(
            RequiredString(element, "id"),
            RequiredString(element, "status"),
            amount.Amount,
            amount.Currency,
            OptionalDate(element, "create_time"));
    }

    private static JsonElement FirstPayment(JsonElement root, string collection)
    {
        if (root.TryGetProperty("purchase_units", out var units) &&
            units.ValueKind == JsonValueKind.Array)
        {
            foreach (var unit in units.EnumerateArray())
            {
                if (unit.TryGetProperty("payments", out var payments) &&
                    payments.TryGetProperty(collection, out var values) &&
                    values.ValueKind == JsonValueKind.Array && values.GetArrayLength() > 0)
                {
                    return values[0];
                }
            }
        }

        throw new PaymentProcessorException("PayPal did not return an authorization for the order.");
    }

    private static bool NeedsPayerAction(JsonElement root)
    {
        if (string.Equals(OptionalString(root, "status"), "PAYER_ACTION_REQUIRED",
            StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!root.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return links.EnumerateArray().Any(x =>
            string.Equals(OptionalString(x, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(OptionalString(x, "rel"), "approve", StringComparison.OrdinalIgnoreCase));
    }

    private static PayPalApiException CreateApiException(HttpStatusCode statusCode, string body)
    {
        string? name = null;
        string? message = null;
        string? debugId = null;
        string? issue = null;
        string? description = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            name = OptionalString(root, "name");
            message = OptionalString(root, "message");
            debugId = OptionalString(root, "debug_id");
            if (root.TryGetProperty("details", out var details) &&
                details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0)
            {
                issue = OptionalString(details[0], "issue");
                description = OptionalString(details[0], "description");
            }
        }
        catch (JsonException)
        {
            // Never include an unstructured upstream body; it can contain sensitive data.
        }

        var safeMessage = string.Join(" ", new[] { name, issue, description, message }
            .Where(x => !string.IsNullOrWhiteSpace(x))!);
        if (string.IsNullOrWhiteSpace(safeMessage))
        {
            safeMessage = $"PayPal returned HTTP {(int)statusCode}.";
        }
        if (!string.IsNullOrWhiteSpace(debugId))
        {
            safeMessage += $" PayPal debug ID: {debugId}.";
        }
        return new PayPalApiException(statusCode, safeMessage, debugId, issue);
    }

    private static JsonElement NestedRequired(JsonElement root, string first, string second)
    {
        if (!root.TryGetProperty(first, out var nested) ||
            !nested.TryGetProperty(second, out var value))
        {
            throw new PaymentProcessorException($"PayPal response omitted {first}.{second}.");
        }
        return value;
    }

    private static string RequiredString(JsonElement element, string name) =>
        OptionalString(element, name) ??
        throw new PaymentProcessorException($"PayPal response omitted {name}.");

    private static string? OptionalString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? OptionalNestedString(JsonElement element, string first, string second) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(first, out var nested)
            ? OptionalString(nested, second)
            : null;

    private static int? OptionalInt(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.TryGetInt32(out var result) ? result : null;

    private static DateTimeOffset? OptionalDate(JsonElement element, string name) =>
        DateTimeOffset.TryParse(OptionalString(element, name), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var result) ? result : null;

    private static decimal? OptionalMoney(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var money))
        {
            return null;
        }
        return decimal.TryParse(OptionalString(money, "value"), NumberStyles.Number,
            CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static (decimal Amount, string Currency) RequiredMoney(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var money) ||
            !decimal.TryParse(OptionalString(money, "value"), NumberStyles.Number,
                CultureInfo.InvariantCulture, out var value))
        {
            throw new PaymentProcessorException($"PayPal response omitted a valid {name}.");
        }
        return (value, RequiredString(money, "currency_code"));
    }
}

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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

// Hand-written client whose wire contract is api-specs/paypal/{checkout_orders_v2,
// payments_payment_v2,vault_payment_tokens_v3,transaction_search_v1}.
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
        _httpClient.BaseAddress = _options.GetBaseUri();
    }

    public async Task<PayPalAuthorization> AuthorizeAsync(
        string externalReference,
        int authorizationAttempt,
        decimal amount,
        string currency,
        PayPalCard? card,
        string? vaultId,
        CancellationToken cancellationToken)
    {
        if ((card is null) == string.IsNullOrWhiteSpace(vaultId))
        {
            throw new ArgumentException("Exactly one card source must be supplied.");
        }

        object paymentSource = card is not null
            ? new { card = CardRequest(card) }
            : new
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
            };

        var request = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = externalReference,
                    custom_id = externalReference,
                    amount = Money(amount, currency)
                }
            },
            payment_source = paymentSource
        };

        using var document = await SendJsonAsync(
            HttpMethod.Post,
            "v2/checkout/orders",
            request,
            $"eshop-pay-{externalReference}-{authorizationAttempt}",
            expectContent: true,
            cancellationToken);

        var root = document!.RootElement;
        ThrowIfPayerActionRequired(root);
        var orderStatus = RequiredString(root, "status");
        var authorization = root.GetProperty("purchase_units")[0]
            .GetProperty("payments").GetProperty("authorizations")[0];
        var sourceCard = OptionalObject(root, "payment_source", "card");

        return new PayPalAuthorization(
            RequiredString(root, "id"),
            orderStatus,
            RequiredString(authorization, "id"),
            RequiredString(authorization, "status"),
            MoneyValue(authorization, "amount"),
            MoneyCurrency(authorization, "amount"),
            OptionalDate(authorization, "create_time") ?? DateTimeOffset.UtcNow,
            OptionalDate(authorization, "expiration_time"),
            OptionalString(sourceCard, "brand"),
            OptionalString(sourceCard, "last_digits"));
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(
            HttpMethod.Get,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}",
            null,
            null,
            expectContent: true,
            cancellationToken);
        return ParseAuthorization(document!.RootElement);
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string externalReference,
        string authorizationId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(
            HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = Money(amount, currency) },
            $"eshop-reauthorize-{externalReference}",
            expectContent: true,
            cancellationToken);
        return ParseAuthorization(document!.RootElement);
    }

    public async Task<PayPalCapture> CaptureAsync(
        string externalReference,
        string authorizationId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(
            HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new { amount = Money(amount, currency), final_capture = true },
            $"eshop-capture-{externalReference}",
            expectContent: true,
            cancellationToken);

        return ParseCapture(document!.RootElement);
    }

    public async Task<PayPalCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(
            HttpMethod.Get,
            $"v2/payments/captures/{Uri.EscapeDataString(captureId)}",
            null,
            null,
            expectContent: true,
            cancellationToken);
        return ParseCapture(document!.RootElement);
    }

    private static PayPalCapture ParseCapture(JsonElement root)
    {
        var breakdown = OptionalObject(root, "seller_receivable_breakdown");
        return new PayPalCapture(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            MoneyValue(root, "amount"),
            MoneyCurrency(root, "amount"),
            OptionalMoneyValue(breakdown, "paypal_fee"),
            OptionalMoneyValue(breakdown, "net_amount"),
            OptionalDate(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task VoidAsync(string externalReference, string authorizationId, CancellationToken cancellationToken)
    {
        using var ignored = await SendJsonAsync(
            HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            null,
            $"eshop-void-{externalReference}",
            expectContent: false,
            cancellationToken);
    }

    public async Task<PayPalRefund> RefundAsync(
        string requestId,
        string captureId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(
            HttpMethod.Post,
            $"v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new { amount = Money(amount, currency) },
            requestId,
            expectContent: true,
            cancellationToken);

        var root = document!.RootElement;
        return new PayPalRefund(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            MoneyValue(root, "amount"),
            MoneyCurrency(root, "amount"),
            OptionalDate(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task<PayPalSavedCard> SaveCardAsync(string buyerId, PayPalCard card, CancellationToken cancellationToken)
    {
        var merchantCustomerId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buyerId))).ToLowerInvariant();
        var requestId = Guid.NewGuid().ToString("N");
        var setupRequest = new
        {
            customer = new { merchant_customer_id = merchantCustomerId },
            payment_source = new { card = CardRequest(card) }
        };

        using var setupDocument = await SendJsonAsync(
            HttpMethod.Post,
            "v3/vault/setup-tokens",
            setupRequest,
            $"eshop-vault-setup-{requestId}",
            expectContent: true,
            cancellationToken);
        var setup = setupDocument!.RootElement;
        ThrowIfPayerActionRequired(setup);
        var setupId = RequiredString(setup, "id");

        var tokenRequest = new
        {
            customer = new { merchant_customer_id = merchantCustomerId },
            payment_source = new { token = new { id = setupId, type = "SETUP_TOKEN" } }
        };
        using var tokenDocument = await SendJsonAsync(
            HttpMethod.Post,
            "v3/vault/payment-tokens",
            tokenRequest,
            $"eshop-vault-token-{requestId}",
            expectContent: true,
            cancellationToken);
        var token = tokenDocument!.RootElement;
        ThrowIfPayerActionRequired(token);
        var tokenCard = token.GetProperty("payment_source").GetProperty("card");
        var customer = OptionalObject(token, "customer");

        return new PayPalSavedCard(
            RequiredString(token, "id"),
            OptionalString(customer, "id"),
            RequiredString(tokenCard, "brand"),
            RequiredString(tokenCard, "last_digits"),
            RequiredString(tokenCard, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken)
    {
        using var ignored = await SendJsonAsync(
            HttpMethod.Delete,
            $"v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}",
            null,
            null,
            expectContent: false,
            cancellationToken);
    }

    public async Task<PayPalTransactionPage> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        int page,
        CancellationToken cancellationToken)
    {
        static string Date(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var path = "v1/reporting/transactions" +
            $"?start_date={Uri.EscapeDataString(Date(from))}" +
            $"&end_date={Uri.EscapeDataString(Date(to))}" +
            "&fields=transaction_info&balance_affecting_records_only=Y&page_size=500" +
            $"&page={page.ToString(CultureInfo.InvariantCulture)}";
        using var document = await SendJsonAsync(HttpMethod.Get, path, null, null, true, cancellationToken);
        var root = document!.RootElement;
        var transactions = new List<PayPalTransaction>();
        if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
        {
            foreach (var detail in details.EnumerateArray())
            {
                var info = detail.GetProperty("transaction_info");
                transactions.Add(new PayPalTransaction(
                    RequiredString(info, "transaction_id"),
                    OptionalString(info, "paypal_reference_id"),
                    OptionalString(info, "transaction_status"),
                    OptionalString(info, "transaction_event_code"),
                    MoneyValue(info, "transaction_amount"),
                    MoneyCurrency(info, "transaction_amount"),
                    OptionalMoneyValue(info, "fee_amount"),
                    OptionalString(info, "invoice_id"),
                    OptionalString(info, "custom_field"),
                    OptionalDate(info, "transaction_initiation_date"),
                    OptionalDate(info, "transaction_updated_date")));
            }
        }

        return new PayPalTransactionPage(
            transactions,
            OptionalInt(root, "page") ?? page,
            OptionalInt(root, "total_pages") ?? 0,
            OptionalDate(root, "last_refreshed_datetime"));
    }

    private async Task<JsonDocument?> SendJsonAsync(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        bool expectContent,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var json = body is null ? null : JsonSerializer.Serialize(body, JsonOptions);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ParseException(response.StatusCode, content);
        }
        if (!expectContent || string.IsNullOrWhiteSpace(content))
        {
            return null;
        }
        return JsonDocument.Parse(content);
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

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw ParseException(response.StatusCode, content);
            }

            using var document = JsonDocument.Parse(content);
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

    private static object CardRequest(PayPalCard card, bool includeVerification = false)
    {
        var billingAddress = new
        {
            address_line_1 = card.BillingAddress.AddressLine1,
            address_line_2 = card.BillingAddress.AddressLine2,
            admin_area_2 = card.BillingAddress.City,
            admin_area_1 = card.BillingAddress.State,
            postal_code = card.BillingAddress.PostalCode,
            country_code = card.BillingAddress.CountryCode.ToUpperInvariant()
        };
        var normalizedNumber = new string(card.Number.Where(char.IsDigit).ToArray());
        return includeVerification
            ? new
            {
                name = card.Name,
                number = normalizedNumber,
                expiry = card.Expiry,
                security_code = card.SecurityCode,
                billing_address = billingAddress,
                verification_method = "SCA_WHEN_REQUIRED"
            }
            : (object)new
            {
                name = card.Name,
                number = normalizedNumber,
                expiry = card.Expiry,
                security_code = card.SecurityCode,
                billing_address = billingAddress
            };
    }

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency.ToUpperInvariant(),
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static PayPalAuthorizationDetails ParseAuthorization(JsonElement root) => new(
        RequiredString(root, "id"),
        RequiredString(root, "status"),
        MoneyValue(root, "amount"),
        MoneyCurrency(root, "amount"),
        OptionalDate(root, "create_time") ?? DateTimeOffset.UtcNow,
        OptionalDate(root, "expiration_time"));

    private static void ThrowIfPayerActionRequired(JsonElement root)
    {
        if (OptionalString(root, "status") == "PAYER_ACTION_REQUIRED")
        {
            throw new PaymentActionRequiredException();
        }
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array &&
            links.EnumerateArray().Any(link =>
                OptionalString(link, "rel") is "payer-action" or "approve"))
        {
            throw new PaymentActionRequiredException();
        }
    }

    private static PayPalException ParseException(HttpStatusCode statusCode, string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            var name = OptionalString(root, "name") ?? OptionalString(root, "error") ?? "PAYPAL_ERROR";
            var message = OptionalString(root, "message") ?? OptionalString(root, "error_description") ?? "PayPal rejected the request.";
            string? issue = null;
            string? description = null;
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                var first = details.EnumerateArray().FirstOrDefault();
                issue = OptionalString(first, "issue");
                description = OptionalString(first, "description");
            }
            var safeMessage = description is null ? message : $"{message} {description}";
            return new PayPalException(statusCode, name, safeMessage, issue, OptionalString(root, "debug_id"));
        }
        catch (JsonException)
        {
            return new PayPalException(statusCode, "PAYPAL_ERROR", "PayPal returned an unreadable error response.", null, null);
        }
    }

    private static string RequiredString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidOperationException($"PayPal response omitted required field '{property}'.");

    private static string? OptionalString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static JsonElement OptionalObject(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var part in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
            {
                return default;
            }
        }
        return current;
    }

    private static DateTimeOffset? OptionalDate(JsonElement element, string property)
        => DateTimeOffset.TryParse(OptionalString(element, property), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;

    private static int? OptionalInt(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;

    private static decimal MoneyValue(JsonElement element, string property)
        => decimal.Parse(RequiredString(element.GetProperty(property), "value"), CultureInfo.InvariantCulture);

    private static string MoneyCurrency(JsonElement element, string property)
        => RequiredString(element.GetProperty(property), "currency_code");

    private static decimal? OptionalMoneyValue(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var money) && money.ValueKind == JsonValueKind.Object &&
           decimal.TryParse(OptionalString(money, "value"), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}

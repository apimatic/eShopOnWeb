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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly HttpClient _http;
    private readonly PayPalOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public PayPalClient(HttpClient http, IOptions<PayPalOptions> options)
    {
        _http = http;
        _options = options.Value;
        _http.BaseAddress = _options.ApiBase;
        _http.Timeout = TimeSpan.FromSeconds(45);
    }

    public async Task<AuthorizationResult> AuthorizeAsync(string orderReference, decimal amount, CardInput? card,
        string? vaultId, string requestId, CancellationToken cancellationToken)
    {
        object paymentSource = card is not null
            ? new { card = CardPayload(card) }
            : new { card = new { vault_id = vaultId } };
        var payload = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[] { new {
                reference_id = $"eshop-{orderReference}",
                invoice_id = $"eshop-{orderReference}",
                amount = Money(amount, _options.Currency)
            } },
            payment_source = paymentSource
        };
        using var document = await SendJsonAsync(HttpMethod.Post, "v2/checkout/orders", payload,
            requestId, cancellationToken);
        ThrowIfPayerAction(document.RootElement, "payment authorization");
        if (TryAuthorization(document.RootElement, out var result)) return result;

        var paypalOrderId = RequiredString(document.RootElement, "id");
        using var authorized = await SendJsonAsync(HttpMethod.Post,
            $"v2/checkout/orders/{Uri.EscapeDataString(paypalOrderId)}/authorize", new { },
            requestId + "-auth", cancellationToken);
        ThrowIfPayerAction(authorized.RootElement, "payment authorization");
        if (TryAuthorization(authorized.RootElement, out result)) return result;
        throw new PayPalApiException(502, "INVALID_PAYPAL_RESPONSE",
            "PayPal did not return an authorization for the order.", null, null);
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = Money(amount, currency) }, requestId, cancellationToken);
        var root = doc.RootElement;
        return new AuthorizationResult(string.Empty, RequiredString(root, "id"),
            RequiredString(root, "status"), Date(root, "create_time") ?? DateTimeOffset.UtcNow,
            Date(root, "expiration_time"));
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new { amount = Money(amount, currency), final_capture = true }, requestId, cancellationToken);
        var root = doc.RootElement;
        var breakdown = root.GetProperty("seller_receivable_breakdown");
        return new CaptureResult(RequiredString(root, "id"), RequiredString(root, "status"),
            Decimal(breakdown.GetProperty("gross_amount")), Decimal(breakdown.GetProperty("paypal_fee")),
            Decimal(breakdown.GetProperty("net_amount")));
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken)
    {
        using var _ = await SendJsonAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void", null,
            requestId, cancellationToken, allowEmpty: true);
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new { amount = Money(amount, currency) }, requestId, cancellationToken);
        var root = doc.RootElement;
        var refundAmount = root.GetProperty("amount");
        return new RefundResult(RequiredString(root, "id"), RequiredString(root, "status"),
            Decimal(refundAmount), RequiredString(refundAmount, "currency_code"));
    }

    public async Task<VaultResult> SaveCardAsync(CardInput card, string requestId,
        CancellationToken cancellationToken)
    {
        using var setup = await SendJsonAsync(HttpMethod.Post, "v3/vault/setup-tokens",
            new { payment_source = new { card = CardPayload(card, includeSecurityCode: false) } },
            requestId + "-setup", cancellationToken);
        ThrowIfPayerAction(setup.RootElement, "card vaulting");
        var setupId = RequiredString(setup.RootElement, "id");
        var customerId = setup.RootElement.TryGetProperty("customer", out var customer)
            ? OptionalString(customer, "id") : null;
        using var token = await SendJsonAsync(HttpMethod.Post, "v3/vault/payment-tokens",
            new { payment_source = new { token = new { id = setupId, type = "SETUP_TOKEN" } } },
            requestId + "-token", cancellationToken);
        var root = token.RootElement;
        var cardResult = root.GetProperty("payment_source").GetProperty("card");
        return new VaultResult(RequiredString(root, "id"), RequiredString(cardResult, "last_digits"),
            RequiredString(cardResult, "brand"), RequiredString(cardResult, "expiry"), customerId);
    }

    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Delete,
            $"v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}", null, null, cancellationToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return;
        if (!response.IsSuccessStatusCode) await ThrowApiError(response, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<PayPalTransaction>();
        var windowStart = from.ToUniversalTime();
        while (windowStart < to.ToUniversalTime())
        {
            var windowEnd = new[] { windowStart.AddDays(31), to.ToUniversalTime() }.Min();
            var page = 1;
            var totalPages = 1;
            do
            {
                var query = $"v1/reporting/transactions?start_date={Uri.EscapeDataString(windowStart.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}" +
                    $"&end_date={Uri.EscapeDataString(windowEnd.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}&fields=all&page_size=500&page={page}";
                using var doc = await SendJsonAsync(HttpMethod.Get, query, null, null, cancellationToken);
                var root = doc.RootElement;
                totalPages = root.TryGetProperty("total_pages", out var pages) ? pages.GetInt32() : 1;
                if (root.TryGetProperty("transaction_details", out var details))
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        var info = detail.GetProperty("transaction_info");
                        decimal? transactionAmount = info.TryGetProperty("transaction_amount", out var money)
                            ? Decimal(money) : null;
                        results.Add(new PayPalTransaction(RequiredString(info, "transaction_id"),
                            OptionalString(info, "invoice_id"), OptionalString(info, "transaction_event_code") ?? string.Empty,
                            OptionalString(info, "transaction_status") ?? string.Empty, transactionAmount,
                            info.TryGetProperty("transaction_amount", out money) ? OptionalString(money, "currency_code") : null,
                            Date(info, "transaction_initiation_date")));
                    }
                }
                page++;
            } while (page <= totalPages);
            windowStart = windowEnd;
        }
        return results.GroupBy(x => new { x.TransactionId, x.EventCode }).Select(x => x.First()).ToList();
    }

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, object? payload,
        string? requestId, CancellationToken cancellationToken, bool allowEmpty = false)
    {
        using var request = await CreateRequestAsync(method, path, payload, requestId, cancellationToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) await ThrowApiError(response, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(content) && allowEmpty
            ? JsonDocument.Parse("{}") : JsonDocument.Parse(content);
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string path, object? payload,
        string? requestId, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessToken(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (path.StartsWith("v1/reporting/", StringComparison.Ordinal))
            request.Headers.TryAddWithoutValidation("PayPal-Enforce-ISO8601-Format", "true");
        if (requestId is not null) request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        if (payload is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json");
        return request;
    }

    private async Task<string> AccessToken(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));
            request.Content = new StringContent("grant_type=client_credentials", Encoding.ASCII,
                "application/x-www-form-urlencoded");
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) await ThrowApiError(response, cancellationToken);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            _accessToken = RequiredString(doc.RootElement, "access_token");
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32());
            return _accessToken;
        }
        finally { _tokenLock.Release(); }
    }

    private static async Task ThrowApiError(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string? issue = null;
            if (root.TryGetProperty("details", out var details) && details.GetArrayLength() > 0)
                issue = OptionalString(details[0], "issue");
            throw new PayPalApiException((int)response.StatusCode, OptionalString(root, "name") ?? "PAYPAL_ERROR",
                OptionalString(root, "message") ?? "PayPal rejected the request.", issue,
                OptionalString(root, "debug_id"));
        }
        catch (JsonException)
        {
            throw new PayPalApiException((int)response.StatusCode, "PAYPAL_ERROR",
                "PayPal returned an error response.", null, null);
        }
    }

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency,
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static object CardPayload(CardInput card, bool includeSecurityCode = true) => new
    {
        number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
        expiry = card.Expiry,
        security_code = includeSecurityCode ? card.SecurityCode : null,
        name = card.Name,
        billing_address = new
        {
            address_line_1 = card.BillingAddress.AddressLine1,
            address_line_2 = card.BillingAddress.AddressLine2,
            admin_area_1 = card.BillingAddress.AdminArea1,
            admin_area_2 = card.BillingAddress.AdminArea2,
            postal_code = card.BillingAddress.PostalCode,
            country_code = card.BillingAddress.CountryCode
        }
    };

    private static bool TryAuthorization(JsonElement root, out AuthorizationResult result)
    {
        if (root.TryGetProperty("purchase_units", out var units) && units.GetArrayLength() > 0 &&
            units[0].TryGetProperty("payments", out var payments) &&
            payments.TryGetProperty("authorizations", out var auths) && auths.GetArrayLength() > 0)
        {
            var auth = auths[0];
            result = new AuthorizationResult(RequiredString(root, "id"), RequiredString(auth, "id"),
                RequiredString(auth, "status"), Date(auth, "create_time") ?? DateTimeOffset.UtcNow,
                Date(auth, "expiration_time"));
            return true;
        }
        result = null!;
        return false;
    }

    private static void ThrowIfPayerAction(JsonElement root, string operation)
    {
        if (OptionalString(root, "status") == "PAYER_ACTION_REQUIRED")
            throw new PayPalPayerActionRequiredException(operation);
    }

    private static string RequiredString(JsonElement element, string property) =>
        element.GetProperty(property).GetString() ?? throw new JsonException($"Missing {property}.");
    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static DateTimeOffset? Date(JsonElement element, string property) =>
        DateTimeOffset.TryParse(OptionalString(element, property), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var value) ? value : null;
    private static decimal Decimal(JsonElement money) =>
        decimal.Parse(RequiredString(money, "value"), NumberStyles.Number, CultureInfo.InvariantCulture);
}

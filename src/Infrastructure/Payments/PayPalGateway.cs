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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public PayPalGateway(HttpClient httpClient, IOptions<PayPalOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(int orderId, decimal amount, string currency,
        PaymentCardData? card, string? vaultId, string requestId, CancellationToken cancellationToken)
    {
        object paymentSource = card is not null
            ? new { card = CardPayload(card) }
            : new { card = new { vault_id = vaultId } };

        var body = new
        {
            intent = "AUTHORIZE",
            payment_source = paymentSource,
            purchase_units = new[]
            {
                new
                {
                    reference_id = $"ESHOP-{orderId}",
                    invoice_id = $"ESHOP-{orderId}-{requestId}",
                    custom_id = $"ESHOP-{orderId}-{requestId}",
                    amount = Money(amount, currency)
                }
            }
        };

        using var json = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", body,
            requestId, cancellationToken);
        ThrowIfPayerActionRequired(json.RootElement, "payment authorization");

        var authorization = json.RootElement.GetProperty("purchase_units")[0]
            .GetProperty("payments").GetProperty("authorizations")[0];
        return ParseAuthorization(authorization, json.RootElement.GetProperty("id").GetString()!);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        using var json = await SendJsonAsync(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null, cancellationToken);
        var orderId = json.RootElement.TryGetProperty("supplementary_data", out var supplementary)
            && supplementary.TryGetProperty("related_ids", out var related)
            && related.TryGetProperty("order_id", out var order)
                ? order.GetString() ?? string.Empty
                : string.Empty;
        return ParseAuthorization(json.RootElement, orderId);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        using var json = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { amount = Money(amount, currency) }, requestId, cancellationToken);
        return ParseAuthorization(json.RootElement, string.Empty);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, int orderId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        using var json = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new
            {
                amount = Money(amount, currency),
                invoice_id = $"ESHOP-{orderId}-{requestId}",
                final_capture = true
            }, requestId, cancellationToken);

        var root = json.RootElement;
        var breakdown = root.GetProperty("seller_receivable_breakdown");
        return new PayPalCaptureResult(
            root.GetProperty("id").GetString()!,
            root.GetProperty("status").GetString()!,
            Decimal(root.GetProperty("amount"), "value"),
            root.GetProperty("amount").GetProperty("currency_code").GetString()!,
            Decimal(breakdown.GetProperty("paypal_fee"), "value"),
            Decimal(breakdown.GetProperty("net_amount"), "value"),
            Date(root, "create_time"));
    }

    public async Task VoidAsync(string authorizationId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            new { }, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent) return;
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        using var json = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new { amount = Money(amount, currency) }, requestId, cancellationToken);
        var root = json.RootElement;
        return new PayPalRefundResult(
            root.GetProperty("id").GetString()!,
            root.GetProperty("status").GetString()!,
            Decimal(root.GetProperty("amount"), "value"),
            root.GetProperty("amount").GetProperty("currency_code").GetString()!,
            Date(root, "create_time"));
    }

    public async Task<PayPalVaultResult> SaveCardAsync(PaymentCardData card, string merchantCustomerId,
        string? paypalCustomerId, string requestId, CancellationToken cancellationToken)
    {
        object customer = string.IsNullOrWhiteSpace(paypalCustomerId)
            ? new { merchant_customer_id = merchantCustomerId }
            : new { id = paypalCustomerId };

        using var setup = await SendJsonAsync(HttpMethod.Post, "/v3/vault/setup-tokens",
            new { payment_source = new { card = CardPayload(card) }, customer },
            requestId + "-setup", cancellationToken);
        ThrowIfPayerActionRequired(setup.RootElement, "card vaulting");

        var setupId = setup.RootElement.GetProperty("id").GetString()!;
        using var token = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens",
            new { payment_source = new { token = new { id = setupId, type = "SETUP_TOKEN" } } },
            requestId + "-token", cancellationToken);
        ThrowIfPayerActionRequired(token.RootElement, "card vaulting");

        var tokenRoot = token.RootElement;
        var savedCard = tokenRoot.GetProperty("payment_source").GetProperty("card");
        return new PayPalVaultResult(
            tokenRoot.GetProperty("id").GetString()!,
            tokenRoot.TryGetProperty("customer", out var savedCustomer)
                && savedCustomer.TryGetProperty("id", out var customerId) ? customerId.GetString() : null,
            savedCard.GetProperty("brand").GetString()!,
            savedCard.GetProperty("last_digits").GetString()!,
            savedCard.GetProperty("expiry").GetString()!);
    }

    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}", null, null, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound) return;
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var all = new List<PayPalTransaction>();
        var cursor = from.ToUniversalTime();
        var final = to.ToUniversalTime();

        while (cursor <= final)
        {
            var windowEnd = cursor.AddDays(30) < final ? cursor.AddDays(30) : final;
            var page = 1;
            var totalPages = 1;
            do
            {
                var query = $"?start_date={Uri.EscapeDataString(ReportDate(cursor))}" +
                    $"&end_date={Uri.EscapeDataString(ReportDate(windowEnd))}" +
                    $"&fields=transaction_info&balance_affecting_records_only=N&page_size=500&page={page}";
                using var json = await SendJsonAsync(HttpMethod.Get,
                    "/v1/reporting/transactions" + query, null, null, cancellationToken,
                    enforceIso8601: true);
                var root = json.RootElement;
                if (root.TryGetProperty("transaction_details", out var details))
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        all.Add(ParseTransaction(detail.GetProperty("transaction_info")));
                    }
                }

                totalPages = root.TryGetProperty("total_pages", out var pages) ? pages.GetInt32() : 1;
                page++;
            } while (page <= totalPages);

            if (windowEnd >= final) break;
            cursor = windowEnd.AddTicks(1);
        }

        return all.Distinct().ToList();
    }

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken, bool enforceIso8601 = false)
    {
        using var response = await SendAsync(method, path, body, requestId, cancellationToken, enforceIso8601);
        await EnsureSuccessAsync(response, cancellationToken);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken, bool enforceIso8601 = false)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrWhiteSpace(requestId))
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        if (enforceIso8601)
            request.Headers.TryAddWithoutValidation("PayPal-Enforce-ISO8601-Format", "true");
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return _accessToken;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
                return _accessToken;

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            _accessToken = json.RootElement.GetProperty("access_token").GetString()!;
            var seconds = json.RootElement.GetProperty("expires_in").GetInt32();
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        string? debugId = null;
        var message = $"PayPal returned HTTP {(int)response.StatusCode}.";
        try
        {
            using var error = JsonDocument.Parse(content);
            var root = error.RootElement;
            debugId = root.TryGetProperty("debug_id", out var debug) ? debug.GetString() : null;
            var name = root.TryGetProperty("name", out var errorName) ? errorName.GetString() : null;
            var description = root.TryGetProperty("message", out var errorMessage) ? errorMessage.GetString() : null;
            var detail = root.TryGetProperty("details", out var details) && details.GetArrayLength() > 0
                ? details[0]
                : default;
            var issue = detail.ValueKind == JsonValueKind.Object && detail.TryGetProperty("issue", out var issueElement)
                ? issueElement.GetString()
                : null;
            var detailDescription = detail.ValueKind == JsonValueKind.Object
                && detail.TryGetProperty("description", out var detailElement)
                ? detailElement.GetString()
                : null;
            message = string.Join(" ", new[] { name, issue, detailDescription ?? description }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
        }
        catch (JsonException)
        {
            // Never include raw provider content: it can contain request echoes.
        }

        if (!string.IsNullOrWhiteSpace(debugId)) message += $" PayPal debug ID: {debugId}.";
        throw new PayPalException(message, (int)response.StatusCode, debugId);
    }

    private string BuildUri(string path) => _options.ResolveBaseUrl().TrimEnd('/') + path;

    private static object CardPayload(PaymentCardData card) => new
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
            country_code = card.BillingAddress.CountryCode
        }
    };

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency,
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static PayPalAuthorizationResult ParseAuthorization(JsonElement root, string orderId)
        => new(
            orderId,
            root.GetProperty("id").GetString()!,
            root.GetProperty("status").GetString()!,
            Decimal(root.GetProperty("amount"), "value"),
            root.GetProperty("amount").GetProperty("currency_code").GetString()!,
            Date(root, "create_time"),
            root.TryGetProperty("expiration_time", out var expiration)
                ? DateTimeOffset.Parse(expiration.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
                : null);

    private static PayPalTransaction ParseTransaction(JsonElement root)
        => new(
            root.GetProperty("transaction_id").GetString()!,
            String(root, "paypal_reference_id"),
            String(root, "paypal_reference_id_type"),
            String(root, "transaction_event_code") ?? string.Empty,
            String(root, "transaction_status") ?? string.Empty,
            Date(root, "transaction_initiation_date"),
            root.TryGetProperty("transaction_amount", out var amount) ? Decimal(amount, "value") : 0,
            root.TryGetProperty("transaction_amount", out amount)
                ? String(amount, "currency_code") ?? string.Empty : string.Empty,
            root.TryGetProperty("fee_amount", out var fee) ? Decimal(fee, "value") : null,
            String(root, "invoice_id"),
            String(root, "custom_field"));

    private static decimal Decimal(JsonElement root, string property)
        => decimal.Parse(root.GetProperty(property).GetString()!, CultureInfo.InvariantCulture);

    private static DateTimeOffset Date(JsonElement root, string property)
        => DateTimeOffset.Parse(root.GetProperty(property).GetString()!, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal);

    private static string? String(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static string ReportDate(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static void ThrowIfPayerActionRequired(JsonElement root, string operation)
    {
        if (root.TryGetProperty("status", out var status)
            && status.GetString() == "PAYER_ACTION_REQUIRED")
            throw new PayPalPayerActionRequiredException(operation);
    }
}

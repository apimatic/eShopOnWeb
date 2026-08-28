using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// A deliberately small client implemented against the OpenAPI documents in api-specs/paypal.
/// It uses only operations and fields present in Checkout Orders v2, Payments v2,
/// Vault Payment Tokens v3, and Transaction Search v1.
/// </summary>
public sealed class PayPalClient : IPayPalClient
{
    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalClient(IHttpClientFactory httpClientFactory, IOptions<PayPalOptions> options)
    {
        _options = options.Value;
        _httpClient = httpClientFactory.CreateClient("PayPal");
        _httpClient.BaseAddress = _options.ResolveBaseAddress();
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<PayPalAuthorization> AuthorizeAsync(int orderId, string paymentRequestId,
        decimal amount, string currency, CardDetails? card, string? vaultId,
        CancellationToken cancellationToken)
    {
        object cardSource;
        if (card is not null)
        {
            cardSource = CardPayload(card);
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
            throw new ArgumentException("A card or saved payment method is required.");
        }

        var requestBody = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = $"order-{orderId}",
                    custom_id = orderId.ToString(CultureInfo.InvariantCulture),
                    invoice_id = $"eshop-{paymentRequestId}",
                    amount = Money(amount, currency)
                }
            },
            payment_source = new { card = cardSource }
        };

        using var response = await SendJsonAsync(HttpMethod.Post, "v2/checkout/orders", requestBody,
            paymentRequestId, cancellationToken);
        using var document = await ReadSuccessAsync(response, cancellationToken);
        var root = document.RootElement;
        var orderStatus = OptionalString(root, "status");
        if (string.Equals(orderStatus, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            HasPayerActionLink(root))
        {
            throw new PayPalPayerActionRequiredException(
                "PayPal requires browser approval for this card payment; headless checkout cannot continue.");
        }

        var authorization = root.GetProperty("purchase_units")[0]
            .GetProperty("payments").GetProperty("authorizations")[0];
        return ParseAuthorization(GetRequiredString(root, "id"), authorization);
    }

    public async Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        var body = new { amount = Money(amount, currency) };
        using var response = await SendJsonAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            body, requestId, cancellationToken);
        using var document = await ReadSuccessAsync(response, cancellationToken);
        return ParseAuthorization(string.Empty, document.RootElement);
    }

    public async Task<PayPalCapture> CaptureAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        var body = new { amount = Money(amount, currency), final_capture = true };
        using var response = await SendJsonAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            body, requestId, cancellationToken);
        using var document = await ReadSuccessAsync(response, cancellationToken);
        var root = document.RootElement;
        var breakdown = root.TryGetProperty("seller_receivable_breakdown", out var value)
            ? value : default;
        var fee = breakdown.ValueKind == JsonValueKind.Object && breakdown.TryGetProperty("paypal_fee", out var feeElement)
            ? ParseMoney(feeElement) : 0m;
        var gross = ParseMoney(root.GetProperty("amount"));
        var net = breakdown.ValueKind == JsonValueKind.Object && breakdown.TryGetProperty("net_amount", out var netElement)
            ? ParseMoney(netElement) : gross - fee;
        return new PayPalCapture(GetRequiredString(root, "id"), GetRequiredString(root, "status"),
            gross, GetRequiredString(root.GetProperty("amount"), "currency_code"), fee, net);
    }

    public async Task VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            null, requestId, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<PayPalRefund> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        var body = new { amount = Money(amount, currency) };
        using var response = await SendJsonAsync(HttpMethod.Post,
            $"v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            body, requestId, cancellationToken);
        using var document = await ReadSuccessAsync(response, cancellationToken);
        var root = document.RootElement;
        var money = root.GetProperty("amount");
        return new PayPalRefund(GetRequiredString(root, "id"), GetRequiredString(root, "status"),
            ParseMoney(money), GetRequiredString(money, "currency_code"));
    }

    public async Task<PayPalPaymentToken> CreatePaymentTokenAsync(string buyerId, CardDetails card,
        string requestId, CancellationToken cancellationToken)
    {
        var body = new
        {
            customer = new { merchant_customer_id = buyerId },
            payment_source = new { card = CardPayload(card) }
        };
        using var response = await SendJsonAsync(HttpMethod.Post, "v3/vault/payment-tokens",
            body, requestId, cancellationToken);
        using var document = await ReadSuccessAsync(response, cancellationToken);
        var root = document.RootElement;
        var savedCard = root.GetProperty("payment_source").GetProperty("card");
        return new PayPalPaymentToken(GetRequiredString(root, "id"),
            OptionalString(savedCard, "brand") ?? "CARD",
            GetRequiredString(savedCard, "last_digits"), OptionalString(savedCard, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(HttpMethod.Delete,
            $"v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}", null, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<PayPalTransaction>();
        var page = 1;
        var totalPages = 1;
        while (page <= totalPages)
        {
            var query = $"v1/reporting/transactions?start_date={EscapeDate(from)}" +
                $"&end_date={EscapeDate(to)}&fields=transaction_info" +
                $"&balance_affecting_records_only=N&page_size=500&page={page}";
            using var response = await SendJsonAsync(HttpMethod.Get, query, null, null, cancellationToken);
            using var document = await ReadSuccessAsync(response, cancellationToken);
            var root = document.RootElement;
            if (root.TryGetProperty("transaction_details", out var transactions))
            {
                foreach (var transaction in transactions.EnumerateArray())
                {
                    results.Add(ParseTransaction(transaction.GetProperty("transaction_info")));
                }
            }

            totalPages = root.TryGetProperty("total_pages", out var pages) ? pages.GetInt32() : 1;
            page++;
        }

        return results;
    }

    private async Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (path.StartsWith("v2/", StringComparison.Ordinal))
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            using var document = await ReadSuccessAsync(response, cancellationToken);
            _accessToken = GetRequiredString(document.RootElement, "access_token");
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiry)
                ? expiry.GetInt32() : 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static object CardPayload(CardDetails card) => new
    {
        name = card.Name,
        number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
        expiry = card.Expiry,
        security_code = card.SecurityCode,
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
        currency_code = currency.ToUpperInvariant(),
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static PayPalAuthorization ParseAuthorization(string orderId, JsonElement root)
    {
        var money = root.GetProperty("amount");
        return new PayPalAuthorization(orderId, GetRequiredString(root, "id"),
            GetRequiredString(root, "status"), ParseMoney(money),
            GetRequiredString(money, "currency_code"),
            OptionalDate(root, "create_time") ?? DateTimeOffset.UtcNow,
            OptionalDate(root, "expiration_time"));
    }

    private static PayPalTransaction ParseTransaction(JsonElement root)
    {
        decimal? amount = null;
        decimal? fee = null;
        string? currency = null;
        if (root.TryGetProperty("transaction_amount", out var amountElement))
        {
            amount = ParseMoney(amountElement);
            currency = OptionalString(amountElement, "currency_code");
        }
        if (root.TryGetProperty("fee_amount", out var feeElement))
        {
            fee = ParseMoney(feeElement);
        }
        return new PayPalTransaction(GetRequiredString(root, "transaction_id"),
            OptionalString(root, "paypal_reference_id"), OptionalString(root, "paypal_reference_id_type"),
            OptionalString(root, "invoice_id"), OptionalString(root, "custom_field"),
            OptionalString(root, "transaction_event_code"), OptionalString(root, "transaction_status"),
            amount, fee, currency, OptionalDate(root, "transaction_initiation_date"),
            OptionalDate(root, "transaction_updated_date"));
    }

    private static bool HasPayerActionLink(JsonElement root) =>
        root.TryGetProperty("links", out var links) && links.EnumerateArray().Any(link =>
            string.Equals(OptionalString(link, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(OptionalString(link, "rel"), "approve", StringComparison.OrdinalIgnoreCase));

    private static string EscapeDate(DateTimeOffset value) =>
        Uri.EscapeDataString(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));

    private static decimal ParseMoney(JsonElement money) =>
        decimal.Parse(GetRequiredString(money, "value"), NumberStyles.Number, CultureInfo.InvariantCulture);

    private static string GetRequiredString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new JsonException($"PayPal response omitted required field '{property}'.");

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static DateTimeOffset? OptionalDate(JsonElement element, string property) =>
        DateTimeOffset.TryParse(OptionalString(element, property), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var value) ? value : null;

    private static async Task<JsonDocument> ReadSuccessAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            await ThrowPayPalErrorAsync(response, cancellationToken);
        }
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            await ThrowPayPalErrorAsync(response, cancellationToken);
        }
    }

    private static async Task ThrowPayPalErrorAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string name = "PAYPAL_ERROR";
        string message = $"PayPal returned HTTP {(int)response.StatusCode}.";
        string? debugId = null;
        var details = new List<string>();
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = document.RootElement;
            name = OptionalString(root, "name") ?? name;
            message = OptionalString(root, "message") ?? message;
            debugId = OptionalString(root, "debug_id");
            if (root.TryGetProperty("details", out var errorDetails))
            {
                foreach (var detail in errorDetails.EnumerateArray())
                {
                    var issue = OptionalString(detail, "issue");
                    var description = OptionalString(detail, "description");
                    details.Add(string.Join(": ", new[] { issue, description }.Where(x => !string.IsNullOrWhiteSpace(x))));
                }
            }
        }
        catch (JsonException)
        {
            // Preserve the status-only message; response bodies are intentionally never copied into logs/errors.
        }
        throw new PayPalApiException((int)response.StatusCode, name, message, debugId, details);
    }
}

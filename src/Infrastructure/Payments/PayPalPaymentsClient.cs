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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalPaymentsClient : IPayPalPaymentsClient
{
    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalPaymentsClient(HttpClient httpClient, IOptions<PayPalOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress = new Uri(ResolveBaseUrl(_options));
        _httpClient.Timeout = TimeSpan.FromSeconds(45);
    }

    public async Task<PayPalAuthorization> AuthorizeAsync(string paymentReference, decimal amount, CardDetails? card,
        string? vaultId, string requestId, CancellationToken cancellationToken)
    {
        if ((card is null) == string.IsNullOrWhiteSpace(vaultId))
            throw new ArgumentException("Supply exactly one card source.");

        object cardSource = card is not null
            ? new
            {
                number = card.Number,
                expiry = card.Expiry,
                security_code = card.SecurityCode,
                name = card.Name,
                billing_address = AddressPayload(card.BillingAddress)
            }
            : new { vault_id = vaultId };

        var externalId = paymentReference;
        var payload = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = externalId,
                    invoice_id = externalId,
                    custom_id = externalId,
                    amount = Money(amount, _options.Currency)
                }
            },
            payment_source = new { card = cardSource }
        };

        using var document = await SendJsonAsync(HttpMethod.Post, "v2/checkout/orders", payload,
            requestId, cancellationToken);
        var root = document.RootElement;
        ThrowIfChallenge(root);
        var orderStatus = Text(root, "status");
        if (!string.Equals(orderStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            throw new PayPalApiException(HttpStatusCode.UnprocessableEntity,
                $"PayPal did not complete the authorization (status {orderStatus}).", orderStatus, null);

        var authorization = root.GetProperty("purchase_units")[0].GetProperty("payments")
            .GetProperty("authorizations")[0];
        return ParseAuthorization(Text(root, "id"), orderStatus, authorization);
    }

    public async Task<PayPalCapture> CaptureAsync(string authorizationId, string paymentReference, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        var payload = new
        {
            amount = Money(amount, currency),
            invoice_id = paymentReference,
            final_capture = true
        };
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            payload, requestId, cancellationToken, preferRepresentation: true);
        var root = document.RootElement;
        var breakdown = root.GetProperty("seller_receivable_breakdown");
        return new PayPalCapture(Text(root, "id"), Text(root, "status"),
            Amount(root.GetProperty("amount")), Text(root.GetProperty("amount"), "currency_code"),
            Amount(breakdown.GetProperty("paypal_fee")), Amount(breakdown.GetProperty("net_amount")),
            Date(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new { }, requestId, cancellationToken, preferRepresentation: true);
        return ParseAuthorization(string.Empty, string.Empty, document.RootElement);
    }

    public async Task<string> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            null, requestId, cancellationToken, preferRepresentation: true);
        if (response.StatusCode == HttpStatusCode.NoContent) return "VOIDED";
        using var document = await ParseSuccessAsync(response, cancellationToken);
        return Text(document.RootElement, "status", "VOIDED");
    }

    public async Task<PayPalRefund> RefundAsync(string captureId, string paymentReference, decimal amount,
        string currency, string idempotencyKey, string requestId, CancellationToken cancellationToken)
    {
        var payload = new
        {
            amount = Money(amount, currency),
            invoice_id = $"{paymentReference}-r-{ShortHash(idempotencyKey)}",
            custom_id = idempotencyKey
        };
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund", payload,
            requestId, cancellationToken, preferRepresentation: true);
        var root = document.RootElement;
        return new PayPalRefund(Text(root, "id"), Text(root, "status"),
            Amount(root.GetProperty("amount")), Text(root.GetProperty("amount"), "currency_code"),
            Date(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task<VaultedCard> SaveCardAsync(string buyerId, string? payPalCustomerId,
        CardDetails card, string requestId, CancellationToken cancellationToken)
    {
        object customer = string.IsNullOrWhiteSpace(payPalCustomerId)
            ? new { merchant_customer_id = MerchantCustomerId(buyerId) }
            : new { id = payPalCustomerId };
        var setupPayload = new
        {
            customer,
            payment_source = new
            {
                card = new
                {
                    number = card.Number,
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    name = card.Name,
                    billing_address = AddressPayload(card.BillingAddress),
                    experience_context = new
                    {
                        return_url = "https://example.invalid/paypal/return",
                        cancel_url = "https://example.invalid/paypal/cancel"
                    }
                }
            }
        };
        using var setup = await SendJsonAsync(HttpMethod.Post, "v3/vault/setup-tokens", setupPayload,
            requestId + "s", cancellationToken);
        ThrowIfChallenge(setup.RootElement);
        var setupStatus = Text(setup.RootElement, "status");
        if (!string.Equals(setupStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
            throw new PayPalApiException(HttpStatusCode.UnprocessableEntity,
                $"PayPal did not approve the card setup (status {setupStatus}).", setupStatus, null);

        var tokenPayload = new
        {
            payment_source = new { token = new { id = Text(setup.RootElement, "id"), type = "SETUP_TOKEN" } }
        };
        using var token = await SendJsonAsync(HttpMethod.Post, "v3/vault/payment-tokens", tokenPayload,
            requestId + "t", cancellationToken);
        var root = token.RootElement;
        var tokenCard = root.GetProperty("payment_source").GetProperty("card");
        return new VaultedCard(Text(root, "id"), Text(root.GetProperty("customer"), "id"),
            Text(tokenCard, "brand", "UNKNOWN"), Text(tokenCard, "last_digits"),
            Text(tokenCard, "expiry"), TextOrNull(tokenCard, "name"));
    }

    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete,
            $"v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}", null, null,
            cancellationToken);
        if (!response.IsSuccessStatusCode) await ThrowPayPalErrorAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<PayPalTransaction>();
        var chunkStart = from.ToUniversalTime();
        var finalEnd = to.ToUniversalTime();
        while (chunkStart < finalEnd)
        {
            var chunkEnd = chunkStart.AddDays(30) < finalEnd ? chunkStart.AddDays(30) : finalEnd;
            var page = 1;
            var totalPages = 1;
            do
            {
                var path = "v1/reporting/transactions?" +
                    $"start_date={Uri.EscapeDataString(FormatDate(chunkStart))}&" +
                    $"end_date={Uri.EscapeDataString(FormatDate(chunkEnd))}&fields=transaction_info&" +
                    $"balance_affecting_records_only=N&page_size=500&page={page}";
                using var response = await SendAsync(HttpMethod.Get, path, null, null, cancellationToken,
                    enforceIsoDates: true);
                using var document = await ParseSuccessAsync(response, cancellationToken);
                var root = document.RootElement;
                if (root.TryGetProperty("total_pages", out var pagesElement) && pagesElement.TryGetInt32(out var pages))
                    totalPages = pages;
                if (root.TryGetProperty("transaction_details", out var details))
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        var item = detail.GetProperty("transaction_info");
                        var money = item.GetProperty("transaction_amount");
                        results.Add(new PayPalTransaction(Text(item, "transaction_id"),
                            TextOrNull(item, "paypal_reference_id"), TextOrNull(item, "invoice_id"),
                            TextOrNull(item, "custom_field"), Text(item, "transaction_event_code", string.Empty),
                            Text(item, "transaction_status", string.Empty), Amount(money),
                            item.TryGetProperty("fee_amount", out var fee) ? Amount(fee) : 0m,
                            Text(money, "currency_code"), Date(item, "transaction_initiation_date") ?? chunkStart,
                            Date(item, "transaction_updated_date") ?? chunkStart));
                    }
                }
                page++;
            } while (page <= totalPages);
            chunkStart = chunkEnd;
        }
        return results.GroupBy(x => new { x.TransactionId, x.EventCode, x.UpdatedAt })
            .Select(x => x.First()).ToList();
    }

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, object body,
        string? requestId, CancellationToken cancellationToken, bool preferRepresentation = false)
    {
        using var response = await SendAsync(method, path, body, requestId, cancellationToken,
            preferRepresentation);
        return await ParseSuccessAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken, bool preferRepresentation = false,
        bool enforceIsoDates = false)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(requestId)) request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        if (preferRepresentation) request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (enforceIsoDates) request.Headers.TryAddWithoutValidation("PayPal-Enforce-ISO8601-Format", "true");
        if (body is not null) request.Content = JsonContent.Create(body);
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        request.Dispose();
        return response;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) await ThrowPayPalErrorAsync(response, cancellationToken);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            _accessToken = Text(document.RootElement, "access_token");
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally { _tokenLock.Release(); }
    }

    private static async Task<JsonDocument> ParseSuccessAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode) await ThrowPayPalErrorAsync(response, cancellationToken);
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
    }

    private static async Task ThrowPayPalErrorAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string? name = null, message = null, issue = null, description = null, debugId = null;
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var root = document.RootElement;
            name = TextOrNull(root, "name") ?? TextOrNull(root, "error");
            message = TextOrNull(root, "message") ?? TextOrNull(root, "error_description");
            debugId = TextOrNull(root, "debug_id");
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0)
            {
                issue = TextOrNull(details[0], "issue");
                description = TextOrNull(details[0], "description");
            }
        }
        catch (JsonException) { }
        if (string.Equals(issue, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            throw new PayPalChallengeRequiredException();
        var safeMessage = description ?? message ?? name ?? $"PayPal returned HTTP {(int)response.StatusCode}.";
        throw new PayPalApiException(response.StatusCode, safeMessage, issue ?? name, debugId);
    }

    private static void ThrowIfChallenge(JsonElement root)
    {
        if (string.Equals(TextOrNull(root, "status"), "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            throw new PayPalChallengeRequiredException();
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array &&
            links.EnumerateArray().Any(x => string.Equals(TextOrNull(x, "rel"), "approve", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(TextOrNull(x, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase)))
            throw new PayPalChallengeRequiredException();
    }

    private static PayPalAuthorization ParseAuthorization(string payPalOrderId, string orderStatus,
        JsonElement authorization)
    {
        var money = authorization.GetProperty("amount");
        return new PayPalAuthorization(payPalOrderId, orderStatus, Text(authorization, "id"),
            Text(authorization, "status"), Amount(money), Text(money, "currency_code"),
            Date(authorization, "create_time") ?? DateTimeOffset.UtcNow,
            Date(authorization, "expiration_time"));
    }

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency.ToUpperInvariant(),
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };
    private static object AddressPayload(BillingAddress x) => new
    {
        address_line_1 = x.AddressLine1,
        address_line_2 = x.AddressLine2,
        admin_area_2 = x.City,
        admin_area_1 = x.State,
        postal_code = x.PostalCode,
        country_code = x.CountryCode.ToUpperInvariant()
    };
    private static decimal Amount(JsonElement money) => decimal.Parse(Text(money, "value"), CultureInfo.InvariantCulture);
    private static string FormatDate(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    private static string MerchantCustomerId(string buyerId) => "eshop_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buyerId))).ToLowerInvariant()[..32];
    private static string ShortHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..16];
    private static string Text(JsonElement element, string property, string? fallback = null) => TextOrNull(element, property) ?? fallback ?? throw new JsonException($"PayPal response omitted {property}.");
    private static string? TextOrNull(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static DateTimeOffset? Date(JsonElement element, string property) => DateTimeOffset.TryParse(TextOrNull(element, property), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) ? value : null;

    private static string ResolveBaseUrl(PayPalOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl)) return options.BaseUrl.TrimEnd('/') + "/";
        return options.Environment.Trim().ToLowerInvariant() switch
        {
            "sandbox" => "https://api-m.sandbox.paypal.com/",
            "live" or "production" => "https://api-m.paypal.com/",
            _ => throw new InvalidOperationException("PayPal:Environment must be Sandbox, Live, or Production.")
        };
    }
}

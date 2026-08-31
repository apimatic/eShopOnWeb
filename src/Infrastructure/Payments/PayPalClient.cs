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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalClient : IPayPalClient
{
    private const int TransactionPageSize = 500;
    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress = new Uri(_options.ResolveBaseUrl() + "/", UriKind.Absolute);
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(int orderId, string externalReference, decimal amount, string currency,
        PayPalPaymentSource paymentSource, string requestId, CancellationToken cancellationToken)
    {
        object source = paymentSource.Card is not null
            ? new { card = ToCardRequest(paymentSource.Card) }
            : new { card = new { vault_id = paymentSource.VaultId } };

        var body = new
        {
            intent = "AUTHORIZE",
            payment_source = source,
            purchase_units = new[]
            {
                new
                {
                    reference_id = orderId.ToString(CultureInfo.InvariantCulture),
                    custom_id = $"eshop-{externalReference}",
                    invoice_id = $"ESHOP-{externalReference}",
                    amount = Money(amount, currency)
                }
            }
        };

        using var document = await SendJsonAsync(HttpMethod.Post, "v2/checkout/orders", body, requestId,
            cancellationToken);
        ThrowIfPayerActionRequired(document.RootElement);
        var root = document.RootElement;
        var authorization = root.GetProperty("purchase_units")[0].GetProperty("payments")
            .GetProperty("authorizations")[0];
        return new PayPalAuthorizationResult(
            RequiredString(root, "id"),
            RequiredString(authorization, "id"),
            RequiredString(authorization, "status"),
            MoneyValue(authorization.GetProperty("amount")),
            RequiredString(authorization.GetProperty("amount"), "currency_code"),
            Date(authorization, "create_time") ?? DateTimeOffset.UtcNow,
            Date(authorization, "expiration_time"));
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        var body = new { amount = Money(amount, currency) };
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize", body, requestId,
            cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("amount", out _))
        {
            var id = RequiredString(root, "id");
            using var full = await SendJsonAsync(HttpMethod.Get,
                $"v2/payments/authorizations/{Uri.EscapeDataString(id)}", null, null, cancellationToken);
            return ParseAuthorization(full.RootElement);
        }

        return ParseAuthorization(root);
    }

    private static PayPalAuthorizationResult ParseAuthorization(JsonElement root)
    {
        var authorizationAmount = root.GetProperty("amount");
        return new PayPalAuthorizationResult(string.Empty, RequiredString(root, "id"),
            RequiredString(root, "status"), MoneyValue(authorizationAmount),
            RequiredString(authorizationAmount, "currency_code"),
            Date(root, "create_time") ?? DateTimeOffset.UtcNow,
            Date(root, "expiration_time"));
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        var body = new { amount = Money(amount, currency), final_capture = true };
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture", body, requestId,
            cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("amount", out _) || !root.TryGetProperty("seller_receivable_breakdown", out _))
        {
            var id = RequiredString(root, "id");
            using var full = await SendJsonAsync(HttpMethod.Get,
                $"v2/payments/captures/{Uri.EscapeDataString(id)}", null, null, cancellationToken);
            return ParseCapture(full.RootElement);
        }

        return ParseCapture(root);
    }

    private static PayPalCaptureResult ParseCapture(JsonElement root)
    {
        var hasBreakdown = root.TryGetProperty("seller_receivable_breakdown", out var breakdown);
        var captureAmount = root.GetProperty("amount");
        return new PayPalCaptureResult(RequiredString(root, "id"), RequiredString(root, "status"),
            MoneyValue(captureAmount), RequiredString(captureAmount, "currency_code"),
            hasBreakdown && breakdown.TryGetProperty("paypal_fee", out var fee) ? MoneyValue(fee) : null,
            hasBreakdown && breakdown.TryGetProperty("net_amount", out var net) ? MoneyValue(net) : null,
            Date(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task<string> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void", new { }, requestId,
            cancellationToken, allowEmptyResponse: true);
        return document.RootElement.ValueKind == JsonValueKind.Object &&
               document.RootElement.TryGetProperty("status", out var status)
            ? status.GetString() ?? "VOIDED"
            : "VOIDED";
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(HttpMethod.Post,
            $"v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new { amount = Money(amount, currency) }, requestId, cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("amount", out _))
        {
            var id = RequiredString(root, "id");
            using var full = await SendJsonAsync(HttpMethod.Get,
                $"v2/payments/refunds/{Uri.EscapeDataString(id)}", null, null, cancellationToken);
            return ParseRefund(full.RootElement);
        }

        return ParseRefund(root);
    }

    private static PayPalRefundResult ParseRefund(JsonElement root)
    {
        var refundAmount = root.GetProperty("amount");
        return new PayPalRefundResult(RequiredString(root, "id"), RequiredString(root, "status"),
            MoneyValue(refundAmount), RequiredString(refundAmount, "currency_code"),
            Date(root, "create_time") ?? DateTimeOffset.UtcNow);
    }

    public async Task<PayPalSavedCardResult> SaveCardAsync(PayPalCard card, string merchantCustomerId,
        string? paypalCustomerId, string requestId, CancellationToken cancellationToken)
    {
        object customer = string.IsNullOrWhiteSpace(paypalCustomerId)
            ? new { merchant_customer_id = merchantCustomerId }
            : new { id = paypalCustomerId };
        var setupBody = new { payment_source = new { card = ToCardRequest(card) }, customer };
        using var setup = await SendJsonAsync(HttpMethod.Post, "v3/vault/setup-tokens", setupBody,
            requestId + "-s", cancellationToken);
        ThrowIfPayerActionRequired(setup.RootElement);
        var setupStatus = OptionalString(setup.RootElement, "status");
        if (!string.Equals(setupStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalApiException(409, "SETUP_TOKEN_NOT_APPROVED",
                $"PayPal did not approve the card setup token (status: {setupStatus ?? "unknown"}).");
        }

        var setupId = RequiredString(setup.RootElement, "id");

        var tokenBody = new
        {
            payment_source = new { token = new { id = setupId, type = "SETUP_TOKEN" } }
        };
        using var token = await SendJsonAsync(HttpMethod.Post, "v3/vault/payment-tokens", tokenBody,
            requestId + "-t", cancellationToken);
        var root = token.RootElement;
        var tokenCard = root.GetProperty("payment_source").GetProperty("card");
        return new PayPalSavedCardResult(RequiredString(root, "id"),
            RequiredString(root.GetProperty("customer"), "id"), RequiredString(tokenCard, "brand"),
            RequiredString(tokenCard, "last_digits"), RequiredString(tokenCard, "expiry"));
    }

    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await SendJsonAsync(HttpMethod.Delete,
                $"v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}", null, null, cancellationToken,
                allowEmptyResponse: true);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            // A remotely missing token already has the requested end state.
        }
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var transactions = new Dictionary<string, PayPalTransaction>(StringComparer.Ordinal);
        var cursor = from.ToUniversalTime();
        var final = to.ToUniversalTime();
        while (cursor <= final)
        {
            var chunkEnd = cursor.AddDays(31).AddSeconds(-1);
            if (chunkEnd > final) chunkEnd = final;
            var page = 1;
            var totalPages = 1;
            do
            {
                var path = "v1/reporting/transactions?" +
                           $"start_date={Uri.EscapeDataString(Iso(cursor))}&" +
                           $"end_date={Uri.EscapeDataString(Iso(chunkEnd))}&" +
                           $"fields=transaction_info&page_size={TransactionPageSize}&page={page}";
                using var document = await SendJsonAsync(HttpMethod.Get, path, null, null, cancellationToken);
                var root = document.RootElement;
                totalPages = root.TryGetProperty("total_pages", out var pages) ? pages.GetInt32() : 1;
                if (root.TryGetProperty("transaction_details", out var details))
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        var info = detail.GetProperty("transaction_info");
                        var id = RequiredString(info, "transaction_id");
                        var amountElement = info.GetProperty("transaction_amount");
                        transactions[id] = new PayPalTransaction(id, OptionalString(info, "paypal_reference_id"),
                            OptionalString(info, "transaction_event_code") ?? string.Empty,
                            OptionalString(info, "transaction_status") ?? string.Empty,
                            MoneyValue(amountElement), info.TryGetProperty("fee_amount", out var fee) ? MoneyValue(fee) : 0m,
                            RequiredString(amountElement, "currency_code"),
                            Date(info, "transaction_initiation_date") ?? cursor,
                            OptionalString(info, "invoice_id"));
                    }
                }

                page++;
            } while (page <= totalPages);

            cursor = chunkEnd.AddSeconds(1);
        }

        return transactions.Values.OrderBy(x => x.InitiatedAt).ToList();
    }

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken, bool allowEmptyResponse = false)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            await GetAccessTokenAsync(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(requestId)) request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        if (method == HttpMethod.Post) request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (path.StartsWith("v1/reporting/", StringComparison.Ordinal))
            request.Headers.TryAddWithoutValidation("PayPal-Enforce-ISO8601-Format", "true");
        if (body is not null) request.Content = JsonContent.Create(body);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode) await ThrowApiErrorAsync(response, cancellationToken);
        if (response.Content.Headers.ContentLength == 0 || response.StatusCode == HttpStatusCode.NoContent)
        {
            return JsonDocument.Parse(allowEmptyResponse ? "{}" : "null");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
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
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode) await ThrowApiErrorAsync(response, cancellationToken);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            _accessToken = RequiredString(document.RootElement, "access_token");
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiry) ? expiry.GetInt32() : 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static async Task ThrowApiErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string code = "PAYPAL_API_ERROR";
        string message = $"PayPal returned HTTP {(int)response.StatusCode}.";
        string? debugId = null;
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            code = OptionalString(root, "name") ?? code;
            message = OptionalString(root, "message") ?? message;
            debugId = OptionalString(root, "debug_id");
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                var issues = details.EnumerateArray().Select(x => OptionalString(x, "issue"))
                    .Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
                if (issues.Length > 0) message += $" Issues: {string.Join(", ", issues)}.";
            }
        }
        catch (JsonException)
        {
            // Keep the sanitized status-only error; never echo an arbitrary response body.
        }

        throw new PayPalApiException((int)response.StatusCode, code, message, debugId);
    }

    private static void ThrowIfPayerActionRequired(JsonElement root)
    {
        var status = OptionalString(root, "status");
        var actionLink = root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array &&
                         links.EnumerateArray().Any(x =>
                             (OptionalString(x, "rel") ?? string.Empty) is "payer-action" or "approve");
        if (status == "PAYER_ACTION_REQUIRED" || actionLink)
        {
            throw new PayPalApiException(409, "PAYER_ACTION_REQUIRED",
                "PayPal requires browser approval for this card; this headless integration cannot continue.",
                payerActionRequired: true);
        }
    }

    private static object ToCardRequest(PayPalCard card) => new
    {
        number = card.Number,
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

    private static decimal MoneyValue(JsonElement money) =>
        decimal.Parse(RequiredString(money, "value"), NumberStyles.Number, CultureInfo.InvariantCulture);

    private static string RequiredString(JsonElement element, string property) =>
        element.GetProperty(property).GetString() ?? throw new JsonException($"PayPal omitted {property}.");

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? Date(JsonElement element, string property) =>
        DateTimeOffset.TryParse(OptionalString(element, property), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

    private static string Iso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

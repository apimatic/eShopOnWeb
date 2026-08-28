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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public sealed class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalSettings _settings;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalClient(IHttpClientFactory httpClientFactory, IOptions<PayPalSettings> settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
    }

    public Task<PayPalOrderResponse> CreateOrderAsync(int orderId, string merchantReference, decimal amount, string currency,
        CancellationToken cancellationToken) =>
        SendJsonAsync<PayPalOrderResponse>(HttpMethod.Post, "/v2/checkout/orders",
            new PayPalCreateOrderRequest("AUTHORIZE", new[]
            {
                new PayPalPurchaseUnitRequest(orderId.ToString(CultureInfo.InvariantCulture),
                    Money(amount, currency), merchantReference, merchantReference)
            }), $"{merchantReference}-create", cancellationToken);

    public Task<PayPalOrderResponse> AuthorizeOrderAsync(string payPalOrderId, PayPalCard card,
        string idempotencyKey, CancellationToken cancellationToken) =>
        SendJsonAsync<PayPalOrderResponse>(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize",
            new PayPalAuthorizeRequest(new PayPalPaymentSource(card)), idempotencyKey, cancellationToken);

    public Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken) =>
        SendJsonAsync<PayPalAuthorization>(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null, cancellationToken);

    public Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken) =>
        SendJsonAsync<PayPalAuthorization>(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new PayPalReauthorizeRequest(Money(amount, currency)), idempotencyKey, cancellationToken);

    public Task<PayPalCapture> CaptureAsync(string authorizationId, string merchantReference, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken) =>
        SendJsonAsync<PayPalCapture>(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new PayPalCaptureRequest(Money(amount, currency), merchantReference, true),
            idempotencyKey, cancellationToken);

    public Task<PayPalCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken) =>
        SendJsonAsync<PayPalCapture>(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null, cancellationToken);

    public Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken) =>
        SendNoContentAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            idempotencyKey, cancellationToken);

    public Task<PayPalRefund> RefundAsync(string captureId, string merchantReference, decimal amount, string currency,
        string idempotencyKey, string? note, CancellationToken cancellationToken) =>
        SendJsonAsync<PayPalRefund>(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            new PayPalRefundRequest(Money(amount, currency), merchantReference, note),
            idempotencyKey, cancellationToken);

    public Task<PayPalRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken) =>
        SendJsonAsync<PayPalRefund>(HttpMethod.Get,
            $"/v2/payments/refunds/{Uri.EscapeDataString(refundId)}", null, null, cancellationToken);

    public Task<PayPalPaymentTokenResponse> CreatePaymentTokenAsync(string buyerId, string? payPalCustomerId,
        PayPalCard card, string idempotencyKey, CancellationToken cancellationToken)
    {
        var customer = string.IsNullOrWhiteSpace(payPalCustomerId)
            ? new PayPalCustomerRequest { MerchantCustomerId = CreateMerchantCustomerId(buyerId) }
            : new PayPalCustomerRequest { Id = payPalCustomerId };
        return SendJsonAsync<PayPalPaymentTokenResponse>(HttpMethod.Post, "/v3/vault/payment-tokens",
            new PayPalPaymentTokenRequest(customer, new PayPalPaymentSource(card)), idempotencyKey, cancellationToken);
    }

    public Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken) =>
        SendNoContentAsync(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(tokenId)}", null, cancellationToken,
            HttpStatusCode.NotFound);

    public async Task<IReadOnlyList<PayPalTransactionDetail>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var transactions = new List<PayPalTransactionDetail>();
        var chunkStart = from;
        while (chunkStart < to)
        {
            var chunkEnd = chunkStart.AddDays(30) < to ? chunkStart.AddDays(30) : to;
            var page = 1;
            int totalPages;
            do
            {
                var path = "/v1/reporting/transactions" +
                    $"?start_date={EncodeDate(chunkStart)}&end_date={EncodeDate(chunkEnd)}" +
                    $"&fields=transaction_info&balance_affecting_records_only=N&page_size=500&page={page}";
                var response = await SendJsonAsync<PayPalTransactionSearchResponse>(HttpMethod.Get,
                    path, null, null, cancellationToken);
                transactions.AddRange(response.TransactionDetails);
                totalPages = response.TotalPages;
                page++;
            } while (page <= totalPages);

            chunkStart = chunkEnd;
        }

        return transactions
            .GroupBy(x => new
            {
                x.TransactionInfo.TransactionId,
                x.TransactionInfo.TransactionEventCode,
                x.TransactionInfo.TransactionInitiationDate,
                x.TransactionInfo.TransactionAmount?.Value
            })
            .Select(x => x.First())
            .ToList();
    }

    private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object? body,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = await CreateRequestAsync(method, path, body, idempotencyKey, cancellationToken);
            using var response = await _httpClientFactory.CreateClient("PayPal").SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _accessToken = null;
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateExceptionAsync(response, cancellationToken);
            }
            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return value ?? throw new PayPalApiException(response.StatusCode, "INVALID_RESPONSE",
                "PayPal returned an empty response.", null);
        }
        throw new PayPalApiException(HttpStatusCode.Unauthorized, "AUTHENTICATION_FAILED",
            "PayPal authentication failed after refreshing the access token.", null);
    }

    private async Task SendNoContentAsync(HttpMethod method, string path, string? idempotencyKey,
        CancellationToken cancellationToken, params HttpStatusCode[] acceptedErrorStatuses)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = await CreateRequestAsync(method, path, null, idempotencyKey, cancellationToken);
            using var response = await _httpClientFactory.CreateClient("PayPal").SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _accessToken = null;
                continue;
            }
            if (response.IsSuccessStatusCode || acceptedErrorStatuses.Contains(response.StatusCode)) return;
            throw await CreateExceptionAsync(response, cancellationToken);
        }
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string path, object? body,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        return request;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}")));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using var response = await _httpClientFactory.CreateClient("PayPal").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
            var token = await response.Content.ReadFromJsonAsync<PayPalTokenResponse>(JsonOptions, cancellationToken);
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
                throw new PayPalApiException(response.StatusCode, "INVALID_TOKEN_RESPONSE",
                    "PayPal did not return an access token.", null);
            _accessToken = token.AccessToken;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private Uri BuildUri(string path) => new($"{_settings.GetBaseUri().ToString().TrimEnd('/')}/{path.TrimStart('/')}");
    private static string EncodeDate(DateTimeOffset value) =>
        Uri.EscapeDataString(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
    private static PayPalMoney Money(decimal value, string currency) =>
        new(currency.ToUpperInvariant(), value.ToString("0.00", CultureInfo.InvariantCulture));
    private static string CreateMerchantCustomerId(string buyerId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buyerId))).ToLowerInvariant();
        return $"eshop-{hash[..32]}";
    }

    private static async Task<PayPalApiException> CreateExceptionAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        string name = "PAYPAL_ERROR", message = $"PayPal returned HTTP {(int)response.StatusCode}.", issue = string.Empty;
        string? debugId = null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.TryGetProperty("name", out var nameElement)) name = nameElement.GetString() ?? name;
            else if (root.TryGetProperty("error", out var errorElement)) name = errorElement.GetString() ?? name;
            if (root.TryGetProperty("message", out var messageElement)) message = messageElement.GetString() ?? message;
            else if (root.TryGetProperty("error_description", out var descriptionElement)) message = descriptionElement.GetString() ?? message;
            if (root.TryGetProperty("debug_id", out var debugElement)) debugId = debugElement.GetString();
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0)
            {
                var first = details[0];
                if (first.TryGetProperty("issue", out var issueElement)) issue = issueElement.GetString() ?? string.Empty;
                if (first.TryGetProperty("description", out var detailDescription)) message = detailDescription.GetString() ?? message;
            }
        }
        catch (JsonException) { }

        return new PayPalApiException(response.StatusCode, name, message, debugId,
            string.IsNullOrEmpty(issue) ? null : issue);
    }
}

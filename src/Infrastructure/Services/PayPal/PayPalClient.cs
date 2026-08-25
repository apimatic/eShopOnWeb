using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Services.PayPal.Models;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

public class PayPalException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }

    public PayPalException(HttpStatusCode statusCode, string message, string responseBody)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

public class PayPalClient
{
    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private string? _accessToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public PayPalClient(HttpClient http, PayPalSettings settings)
    {
        _http = http;
        _settings = settings;
        _http.BaseAddress = new Uri(_settings.ResolvedBaseUrl + "/");
    }

    private async Task<string> GetAccessTokenAsync()
    {
        if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiry)
            return _accessToken;

        await _tokenLock.WaitAsync();
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiry)
                return _accessToken;

            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));

            var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            var response = await _http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new PayPalException(response.StatusCode, $"PayPal OAuth failed: {body}", body);

            var token = JsonSerializer.Deserialize<OAuthTokenResponse>(body)!;
            _accessToken = token.AccessToken;
            // Refresh 60 seconds before expiry
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn - 60);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<HttpRequestMessage> AuthorizedRequest(HttpMethod method, string path)
    {
        var token = await GetAccessTokenAsync();
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request)
    {
        var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new PayPalException(response.StatusCode, $"PayPal API error {(int)response.StatusCode}: {body}", body);
        return JsonSerializer.Deserialize<T>(body)!;
    }

    private async Task SendNoContentAsync(HttpRequestMessage request)
    {
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NoContent)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new PayPalException(response.StatusCode, $"PayPal API error {(int)response.StatusCode}: {body}", body);
        }
    }

    // Orders v2

    public async Task<CreateOrderResponse> CreateOrderAsync(string currency, decimal amount, int orderId, string idempotencyKey)
    {
        var request = await AuthorizedRequest(HttpMethod.Post, "/v2/checkout/orders");
        request.Headers.Add("PayPal-Request-Id", idempotencyKey);
        request.Content = JsonContent.Create(new CreateOrderRequest(
            Intent: "AUTHORIZE",
            PurchaseUnits: new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest(
                    Amount: new PayPalAmount(currency, amount.ToString("F2")),
                    CustomId: $"eshop-order-{orderId}")
            }
        ), options: _jsonOptions);
        return await SendAsync<CreateOrderResponse>(request);
    }

    public async Task<AuthorizeOrderResponse> AuthorizeOrderWithCardAsync(
        string payPalOrderId, string cardNumber, string expiry, string? cvv,
        string? cardholderName, string billingCountryCode, string? billingPostalCode,
        string idempotencyKey)
    {
        var request = await AuthorizedRequest(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize");
        request.Headers.Add("PayPal-Request-Id", idempotencyKey);
        request.Content = JsonContent.Create(new AuthorizeOrderRequest(
            PaymentSource: new PaymentSourceRequest
            {
                Card = new CardPaymentSource(
                    Name: cardholderName,
                    Number: cardNumber,
                    Expiry: expiry,
                    SecurityCode: cvv,
                    BillingAddress: new CardBillingAddress(
                        CountryCode: billingCountryCode,
                        PostalCode: billingPostalCode))
            }
        ), options: _jsonOptions);
        return await SendAsync<AuthorizeOrderResponse>(request);
    }

    public async Task<AuthorizeOrderResponse> AuthorizeOrderWithTokenAsync(
        string payPalOrderId, string vaultTokenId, string idempotencyKey)
    {
        var request = await AuthorizedRequest(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize");
        request.Headers.Add("PayPal-Request-Id", idempotencyKey);
        request.Content = JsonContent.Create(new AuthorizeOrderRequest(
            PaymentSource: new PaymentSourceRequest
            {
                Token = new TokenPaymentSource(Id: vaultTokenId, Type: "PAYMENT_METHOD_TOKEN")
            }
        ), options: _jsonOptions);
        return await SendAsync<AuthorizeOrderResponse>(request);
    }

    // Payments v2 - Authorization

    public async Task<AuthorizationResponse> GetAuthorizationAsync(string authorizationId)
    {
        var request = await AuthorizedRequest(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}");
        return await SendAsync<AuthorizationResponse>(request);
    }

    public async Task<AuthorizationResponse> ReauthorizeAsync(string authorizationId, string currency, decimal amount)
    {
        var request = await AuthorizedRequest(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize");
        request.Content = JsonContent.Create(new ReauthorizeRequest
        {
            Amount = new PayPalAmount(currency, amount.ToString("F2"))
        }, options: _jsonOptions);
        return await SendAsync<AuthorizationResponse>(request);
    }

    public async Task<CaptureDetail> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey)
    {
        var request = await AuthorizedRequest(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture");
        request.Headers.Add("PayPal-Request-Id", idempotencyKey);
        request.Content = JsonContent.Create(new CaptureAuthorizationRequest(FinalCapture: true), options: _jsonOptions);
        return await SendAsync<CaptureDetail>(request);
    }

    public async Task VoidAuthorizationAsync(string authorizationId)
    {
        var request = await AuthorizedRequest(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void");
        await SendNoContentAsync(request);
    }

    // Payments v2 - Refund

    public async Task<RefundResponse> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey)
    {
        var request = await AuthorizedRequest(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund");
        request.Headers.Add("PayPal-Request-Id", idempotencyKey);
        request.Content = JsonContent.Create(new RefundCaptureRequest
        {
            Amount = amount.HasValue ? new PayPalAmount(currency, amount.Value.ToString("F2")) : null
        }, options: _jsonOptions);
        return await SendAsync<RefundResponse>(request);
    }

    // Vault v3

    public async Task<VaultTokenResponse> CreateVaultTokenAsync(
        string cardNumber, string expiry, string? cvv, string? cardholderName,
        string billingCountryCode, string? billingPostalCode, string merchantCustomerId)
    {
        var request = await AuthorizedRequest(HttpMethod.Post, "/v3/vault/payment-tokens");
        request.Content = JsonContent.Create(new CreateVaultTokenRequest(
            PaymentSource: new VaultPaymentSource(
                Card: new VaultCardSource(
                    Name: cardholderName,
                    Number: cardNumber,
                    Expiry: expiry,
                    SecurityCode: cvv,
                    BillingAddress: new VaultBillingAddress(
                        CountryCode: billingCountryCode,
                        PostalCode: billingPostalCode))),
            Customer: new VaultCustomer(MerchantCustomerId: merchantCustomerId)
        ), options: _jsonOptions);
        return await SendAsync<VaultTokenResponse>(request);
    }

    public async Task DeleteVaultTokenAsync(string vaultTokenId)
    {
        var request = await AuthorizedRequest(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultTokenId}");
        await SendNoContentAsync(request);
    }

    // Transaction Search v1

    public async Task<TransactionSearchResponse> SearchTransactionsAsync(
        DateTimeOffset startDate, DateTimeOffset endDate, int page = 1, int pageSize = 500)
    {
        var start = Uri.EscapeDataString(startDate.ToString("yyyy-MM-ddTHH:mm:sszzz"));
        var end = Uri.EscapeDataString(endDate.ToString("yyyy-MM-ddTHH:mm:sszzz"));
        var url = $"/v1/reporting/transactions?start_date={start}&end_date={end}&fields=all&page_size={pageSize}&page={page}&total_required=true";
        var request = await AuthorizedRequest(HttpMethod.Get, url);
        return await SendAsync<TransactionSearchResponse>(request);
    }
}

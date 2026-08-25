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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalException : Exception
{
    public int StatusCode { get; }
    public string? PayPalName { get; }
    public string? DebugId { get; }
    public List<PayPalErrorDetail> Details { get; }

    public PayPalException(int statusCode, string message, string? payPalName = null,
        string? debugId = null, List<PayPalErrorDetail>? details = null)
        : base(message)
    {
        StatusCode = statusCode;
        PayPalName = payPalName;
        DebugId = debugId;
        Details = details ?? new();
    }

    public bool IsAuthorizationExpired() =>
        Details.Exists(d => d.Issue != null && (
            d.Issue.Contains("AUTHORIZATION_VALIDITY_PERIOD_EXPIRED", StringComparison.OrdinalIgnoreCase) ||
            d.Issue.Contains("AUTHORIZATION_ALREADY_VOIDED", StringComparison.OrdinalIgnoreCase)));
}

public class PayerActionRequiredException : Exception
{
    public string ApprovalUrl { get; }
    public PayerActionRequiredException(string approvalUrl)
        : base("PayPal requires payer action (e.g. 3DS challenge) for this transaction. Browser approval is not supported.")
    {
        ApprovalUrl = approvalUrl;
    }
}

public class PayPalClient
{
    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalClient> _logger;

    private string? _accessToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public PayPalClient(IOptions<PayPalSettings> settings, ILogger<PayPalClient> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _httpClient = new HttpClient();
    }

    // ---- Token management ----

    private async Task<string> GetAccessTokenAsync()
    {
        if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
            return _accessToken;

        await _tokenLock.WaitAsync();
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
                return _accessToken;

            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.GetBaseUrl()}/v1/oauth2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            var response = await _httpClient.SendAsync(request);
            await EnsureSuccessAsync(response, "GetAccessToken");

            var tokenResponse = await response.Content.ReadFromJsonAsync<PayPalTokenResponse>(_jsonOptions)
                ?? throw new PayPalException(0, "Failed to parse token response");

            _accessToken = tokenResponse.AccessToken;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
            _logger.LogDebug("PayPal access token refreshed, expires at {Expiry}", _tokenExpiry);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void InvalidateToken()
    {
        _accessToken = null;
        _tokenExpiry = DateTimeOffset.MinValue;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("PayPal {Operation} failed [{Status}]: {Body}", operation, (int)response.StatusCode, body);

        PayPalErrorResponse? error = null;
        try { error = JsonSerializer.Deserialize<PayPalErrorResponse>(body, _jsonOptions); } catch { }

        throw new PayPalException(
            (int)response.StatusCode,
            error?.Message ?? $"PayPal {operation} failed with status {response.StatusCode}",
            error?.Name,
            error?.DebugId,
            error?.Details);
    }

    private async Task<T> ExecuteWithTokenRefreshAsync<T>(Func<string, Task<T>> action)
    {
        var token = await GetAccessTokenAsync();
        try
        {
            return await action(token);
        }
        catch (PayPalException ex) when (ex.StatusCode == 401)
        {
            InvalidateToken();
            token = await GetAccessTokenAsync();
            return await action(token);
        }
    }

    // ---- Orders ----

    public async Task<PayPalOrderResponse> CreateOrderWithCardAsync(
        decimal amount, string currency, int orderId,
        string cardNumber, string cardExpiry, string cardCvv, string cardName,
        PayPalAddress billingAddress, string idempotencyKey)
    {
        return await ExecuteWithTokenRefreshAsync(async token =>
        {
            var url = $"{_settings.GetBaseUrl()}/v2/checkout/orders";

            var body = new PayPalCreateOrderRequest
            {
                Intent = "AUTHORIZE",
                PurchaseUnits = new List<PayPalPurchaseUnit>
                {
                    new()
                    {
                        Amount = new PayPalAmount { CurrencyCode = currency, Value = amount.ToString("F2") },
                        CustomId = orderId.ToString()
                    }
                },
                PaymentSource = new PayPalOrderPaymentSource
                {
                    Card = new PayPalCardSource
                    {
                        Number = cardNumber,
                        Expiry = cardExpiry,
                        SecurityCode = cardCvv,
                        Name = cardName,
                        BillingAddress = billingAddress
                    }
                }
            };

            var request = CreateRequest(HttpMethod.Post, url, token);
            request.Headers.Add("PayPal-Request-Id", idempotencyKey);
            request.Headers.Add("Prefer", "return=representation");
            request.Content = JsonContent.Create(body, options: _jsonOptions);

            var response = await _httpClient.SendAsync(request);
            await EnsureSuccessAsync(response, "CreateOrderWithCard");

            var result = await response.Content.ReadFromJsonAsync<PayPalOrderResponse>(_jsonOptions)
                ?? throw new PayPalException(0, "Failed to parse order response");

            CheckForPayerAction(result);
            return result;
        });
    }

    public async Task<PayPalOrderResponse> CreateOrderWithVaultAsync(
        decimal amount, string currency, int orderId,
        string vaultTokenId, string idempotencyKey)
    {
        return await ExecuteWithTokenRefreshAsync(async token =>
        {
            var url = $"{_settings.GetBaseUrl()}/v2/checkout/orders";

            var body = new PayPalCreateOrderRequest
            {
                Intent = "AUTHORIZE",
                PurchaseUnits = new List<PayPalPurchaseUnit>
                {
                    new()
                    {
                        Amount = new PayPalAmount { CurrencyCode = currency, Value = amount.ToString("F2") },
                        CustomId = orderId.ToString()
                    }
                },
                PaymentSource = new PayPalOrderPaymentSource
                {
                    Card = new PayPalCardSource
                    {
                        VaultId = vaultTokenId,
                        StoredCredential = new PayPalStoredCredential
                        {
                            PaymentInitiator = "CUSTOMER",
                            PaymentType = "UNSCHEDULED",
                            Usage = "SUBSEQUENT"
                        }
                    }
                }
            };

            var request = CreateRequest(HttpMethod.Post, url, token);
            request.Headers.Add("PayPal-Request-Id", idempotencyKey);
            request.Headers.Add("Prefer", "return=representation");
            request.Content = JsonContent.Create(body, options: _jsonOptions);

            var response = await _httpClient.SendAsync(request);
            await EnsureSuccessAsync(response, "CreateOrderWithVault");

            var result = await response.Content.ReadFromJsonAsync<PayPalOrderResponse>(_jsonOptions)
                ?? throw new PayPalException(0, "Failed to parse order response");

            CheckForPayerAction(result);
            return result;
        });
    }

    private static void CheckForPayerAction(PayPalOrderResponse order)
    {
        foreach (var link in order.Links)
        {
            if (link.Rel.Equals("payer-action", StringComparison.OrdinalIgnoreCase) ||
                link.Rel.Equals("approve", StringComparison.OrdinalIgnoreCase))
            {
                throw new PayerActionRequiredException(link.Href);
            }
        }
    }

    // ---- Authorizations ----

    public async Task<PayPalCaptureResponse> CaptureAuthorizationAsync(
        string authorizationId, string idempotencyKey)
    {
        return await ExecuteWithTokenRefreshAsync(async token =>
        {
            var url = $"{_settings.GetBaseUrl()}/v2/payments/authorizations/{authorizationId}/capture";

            var body = new PayPalCaptureRequest { FinalCapture = true };

            var request = CreateRequest(HttpMethod.Post, url, token);
            request.Headers.Add("PayPal-Request-Id", idempotencyKey);
            request.Headers.Add("Prefer", "return=representation");
            request.Content = JsonContent.Create(body, options: _jsonOptions);

            var response = await _httpClient.SendAsync(request);
            await EnsureSuccessAsync(response, "CaptureAuthorization");

            return await response.Content.ReadFromJsonAsync<PayPalCaptureResponse>(_jsonOptions)
                ?? throw new PayPalException(0, "Failed to parse capture response");
        });
    }

    public async Task<PayPalReauthorizeResponse> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, string idempotencyKey)
    {
        return await ExecuteWithTokenRefreshAsync(async token =>
        {
            var url = $"{_settings.GetBaseUrl()}/v2/payments/authorizations/{authorizationId}/reauthorize";

            var body = new PayPalReauthorizeRequest
            {
                Amount = new PayPalAmount { CurrencyCode = currency, Value = amount.ToString("F2") }
            };

            var request = CreateRequest(HttpMethod.Post, url, token);
            request.Headers.Add("PayPal-Request-Id", idempotencyKey);
            request.Headers.Add("Prefer", "return=representation");
            request.Content = JsonContent.Create(body, options: _jsonOptions);

            var response = await _httpClient.SendAsync(request);
            await EnsureSuccessAsync(response, "ReauthorizePayment");

            return await response.Content.ReadFromJsonAsync<PayPalReauthorizeResponse>(_jsonOptions)
                ?? throw new PayPalException(0, "Failed to parse reauthorize response");
        });
    }

    public async Task VoidAuthorizationAsync(string authorizationId)
    {
        await ExecuteWithTokenRefreshAsync(async token =>
        {
            var url = $"{_settings.GetBaseUrl()}/v2/payments/authorizations/{authorizationId}/void";

            var request = CreateRequest(HttpMethod.Post, url, token);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            // 204 No Content is success for void
            if (response.StatusCode == HttpStatusCode.NoContent || response.IsSuccessStatusCode)
                return true;

            await EnsureSuccessAsync(response, "VoidAuthorization");
            return true;
        });
    }

    // ---- Refunds ----

    public async Task<PayPalRefundResponse> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey)
    {
        return await ExecuteWithTokenRefreshAsync(async token =>
        {
            var url = $"{_settings.GetBaseUrl()}/v2/payments/captures/{captureId}/refund";

            PayPalRefundRequest body;
            if (amount.HasValue)
            {
                body = new PayPalRefundRequest
                {
                    Amount = new PayPalAmount { CurrencyCode = currency, Value = amount.Value.ToString("F2") }
                };
            }
            else
            {
                body = new PayPalRefundRequest();
            }

            var request = CreateRequest(HttpMethod.Post, url, token);
            request.Headers.Add("PayPal-Request-Id", idempotencyKey);
            request.Headers.Add("Prefer", "return=representation");
            request.Content = JsonContent.Create(body, options: _jsonOptions);

            var response = await _httpClient.SendAsync(request);
            await EnsureSuccessAsync(response, "RefundCapture");

            return await response.Content.ReadFromJsonAsync<PayPalRefundResponse>(_jsonOptions)
                ?? throw new PayPalException(0, "Failed to parse refund response");
        });
    }

    // ---- Vault ----

    public async Task<PayPalCreatePaymentTokenResponse> CreateVaultPaymentTokenAsync(
        string cardNumber, string cardExpiry, string cardCvv, string cardName,
        PayPalAddress? billingAddress, string buyerId, string idempotencyKey)
    {
        return await ExecuteWithTokenRefreshAsync(async token =>
        {
            var url = $"{_settings.GetBaseUrl()}/v3/vault/payment-tokens";

            var body = new PayPalCreatePaymentTokenRequest
            {
                PaymentSource = new PayPalVaultPaymentSource
                {
                    Card = new PayPalVaultCard
                    {
                        Number = cardNumber,
                        Expiry = cardExpiry,
                        SecurityCode = cardCvv,
                        Name = cardName,
                        BillingAddress = billingAddress
                    }
                },
                Customer = new PayPalVaultCustomer { MerchantCustomerId = buyerId }
            };

            var request = CreateRequest(HttpMethod.Post, url, token);
            request.Headers.Add("PayPal-Request-Id", idempotencyKey);
            request.Content = JsonContent.Create(body, options: _jsonOptions);

            var response = await _httpClient.SendAsync(request);
            await EnsureSuccessAsync(response, "CreateVaultPaymentToken");

            var result = await response.Content.ReadFromJsonAsync<PayPalCreatePaymentTokenResponse>(_jsonOptions)
                ?? throw new PayPalException(0, "Failed to parse vault token response");

            // Check if buyer approval is required (3DS on vault)
            foreach (var link in result.Links)
            {
                if (link.Rel.Equals("payer-action", StringComparison.OrdinalIgnoreCase) ||
                    link.Rel.Equals("approve", StringComparison.OrdinalIgnoreCase))
                {
                    throw new PayerActionRequiredException(link.Href);
                }
            }

            return result;
        });
    }

    public async Task DeleteVaultPaymentTokenAsync(string tokenId)
    {
        await ExecuteWithTokenRefreshAsync(async token =>
        {
            var url = $"{_settings.GetBaseUrl()}/v3/vault/payment-tokens/{tokenId}";

            var request = CreateRequest(HttpMethod.Delete, url, token);
            var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.NoContent || response.IsSuccessStatusCode)
                return true;

            await EnsureSuccessAsync(response, "DeleteVaultPaymentToken");
            return true;
        });
    }

    // ---- Transaction Search (with pagination) ----

    public async Task<List<PayPalTransactionDetail>> GetAllTransactionsAsync(
        DateTimeOffset startDate, DateTimeOffset endDate)
    {
        var allTransactions = new List<PayPalTransactionDetail>();

        // PayPal max range is 31 days per call; split into chunks
        var chunks = SplitDateRange(startDate, endDate, days: 31);

        foreach (var (chunkStart, chunkEnd) in chunks)
        {
            var chunkTransactions = await GetTransactionPagedAsync(chunkStart, chunkEnd);
            allTransactions.AddRange(chunkTransactions);
        }

        return allTransactions;
    }

    private async Task<List<PayPalTransactionDetail>> GetTransactionPagedAsync(
        DateTimeOffset startDate, DateTimeOffset endDate)
    {
        var all = new List<PayPalTransactionDetail>();
        int page = 1;
        const int pageSize = 500;
        int totalPages = 1;

        do
        {
            var currentPage = page;
            var result = await ExecuteWithTokenRefreshAsync(async token =>
            {
                var start = Uri.EscapeDataString(startDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
                var end = Uri.EscapeDataString(endDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
                var url = $"{_settings.GetBaseUrl()}/v1/reporting/transactions" +
                          $"?start_date={start}&end_date={end}" +
                          $"&page_size={pageSize}&page={currentPage}&fields=all&balance_affecting_records_only=N";

                var request = CreateRequest(HttpMethod.Get, url, token);
                var response = await _httpClient.SendAsync(request);
                await EnsureSuccessAsync(response, "ListTransactions");

                return await response.Content.ReadFromJsonAsync<PayPalTransactionSearchResponse>(_jsonOptions)
                    ?? throw new PayPalException(0, "Failed to parse transaction response");
            });

            all.AddRange(result.TransactionDetails);
            totalPages = result.TotalPages > 0 ? result.TotalPages : 1;
            page++;
        } while (page <= totalPages);

        return all;
    }

    private static List<(DateTimeOffset Start, DateTimeOffset End)> SplitDateRange(
        DateTimeOffset start, DateTimeOffset end, int days)
    {
        var chunks = new List<(DateTimeOffset, DateTimeOffset)>();
        var current = start;
        while (current < end)
        {
            var chunkEnd = current.AddDays(days);
            if (chunkEnd > end) chunkEnd = end;
            chunks.Add((current, chunkEnd));
            current = chunkEnd;
        }
        if (chunks.Count == 0) chunks.Add((start, end));
        return chunks;
    }
}

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalClient> _logger;

    private string? _accessToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public PayPalClient(IHttpClientFactory httpClientFactory, IOptions<PayPalSettings> settings, ILogger<PayPalClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    private string BaseUrl =>
        !string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? _settings.BaseUrl.TrimEnd('/')
            : string.Equals(_settings.Environment, "live", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.paypal.com"
                : "https://api-m.sandbox.paypal.com";

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiry)
            return _accessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiry)
                return _accessToken;

            using var client = _httpClientFactory.CreateClient();
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/oauth2/token");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            req.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var resp = await client.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new PayPalException($"PayPal token request failed ({(int)resp.StatusCode})", body, (int)resp.StatusCode);

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(body, _json)
                ?? throw new PayPalException("Null response from PayPal token endpoint");

            _accessToken = token.AccessToken;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn - 60);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<HttpRequestMessage> BuildRequestAsync(HttpMethod method, string path, string? idempotencyKey, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);
        var req = new HttpRequestMessage(method, $"{BaseUrl}{path}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (idempotencyKey != null)
            req.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        return req;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage req, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient();
        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            var err = TryParseError(body);
            var msg = err?.Message ?? $"PayPal API error {(int)resp.StatusCode}";
            var details = err?.Details;
            _logger.LogWarning("PayPal API error {StatusCode}: {Message} {Body}", (int)resp.StatusCode, msg, body);
            throw new PayPalException($"PayPal: {msg}", body, (int)resp.StatusCode);
        }

        return JsonSerializer.Deserialize<T>(body, _json)
            ?? throw new PayPalException($"PayPal returned null for {req.RequestUri}");
    }

    private static PayPalErrorResponse? TryParseError(string body)
    {
        try { return JsonSerializer.Deserialize<PayPalErrorResponse>(body, _json); }
        catch { return null; }
    }

    private static void CheckForChallenge(PayPalOrderResponse order)
    {
        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            throw new PayPalChallengeRequiredException();
    }

    // ── Orders API ──────────────────────────────────────────────────────────

    public async Task<PayPalOrderResponse> CreateOrderAsync(string amount, string currency, string customId, string idempotencyKey, CancellationToken ct = default)
    {
        var req = await BuildRequestAsync(HttpMethod.Post, "/v2/checkout/orders", idempotencyKey, ct);
        req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        var body = new PayPalCreateOrderRequest(
            "AUTHORIZE",
            new List<PayPalPurchaseUnitRequest>
            {
                new PayPalPurchaseUnitRequest(
                    new PayPalMoney(currency, amount),
                    CustomId: customId
                )
            }
        );
        req.Content = new StringContent(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json");
        return await SendAsync<PayPalOrderResponse>(req, ct);
    }

    public async Task<PayPalOrderResponse> AuthorizeOrderAsync(string paypalOrderId, PayPalCardSource cardSource, string idempotencyKey, CancellationToken ct = default)
    {
        var req = await BuildRequestAsync(HttpMethod.Post, $"/v2/checkout/orders/{paypalOrderId}/authorize", idempotencyKey, ct);
        req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        var body = new PayPalAuthorizeOrderRequest(new PayPalOrderPaymentSource(cardSource));
        req.Content = new StringContent(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json");
        var result = await SendAsync<PayPalOrderResponse>(req, ct);
        CheckForChallenge(result);
        return result;
    }

    // ── Payments API ─────────────────────────────────────────────────────────

    public async Task<PayPalGetAuthorizationResponse> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        var req = await BuildRequestAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, ct);
        return await SendAsync<PayPalGetAuthorizationResponse>(req, ct);
    }

    public async Task<PayPalCaptureResponse> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        var req = await BuildRequestAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", idempotencyKey, ct);
        req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        var body = new PayPalCaptureRequest(FinalCapture: true);
        req.Content = new StringContent(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json");
        return await SendAsync<PayPalCaptureResponse>(req, ct);
    }

    public async Task<PayPalReauthorizeResponse> ReauthorizeAsync(string authorizationId, string amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        var req = await BuildRequestAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", idempotencyKey, ct);
        req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        var body = new PayPalReauthorizeRequest(new PayPalMoney(currency, amount));
        req.Content = new StringContent(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json");
        return await SendAsync<PayPalReauthorizeResponse>(req, ct);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        var req = await BuildRequestAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, ct);
        req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var client = _httpClientFactory.CreateClient();
        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.NoContent)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new PayPalException($"PayPal void failed ({(int)resp.StatusCode})", body, (int)resp.StatusCode);
        }
    }

    public async Task<PayPalRefundResponse> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        var req = await BuildRequestAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", idempotencyKey, ct);
        req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        var body = new PayPalRefundRequest(
            Amount: amount.HasValue ? new PayPalMoney(currency, amount.Value.ToString("F2")) : null
        );
        req.Content = new StringContent(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json");
        return await SendAsync<PayPalRefundResponse>(req, ct);
    }

    // ── Vault API ─────────────────────────────────────────────────────────────

    public async Task<PayPalVaultTokenResponse> CreateVaultPaymentTokenAsync(PayPalVaultCardRequest card, string customerId, string idempotencyKey, CancellationToken ct = default)
    {
        var req = await BuildRequestAsync(HttpMethod.Post, "/v3/vault/payment-tokens", idempotencyKey, ct);
        var body = new PayPalCreateVaultTokenRequest(
            new PayPalVaultPaymentSource(card),
            new PayPalVaultCustomer(customerId)
        );
        req.Content = new StringContent(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json");
        return await SendAsync<PayPalVaultTokenResponse>(req, ct);
    }

    public async Task<List<PayPalVaultTokenResponse>> ListVaultPaymentTokensAsync(string customerId, CancellationToken ct = default)
    {
        var result = new List<PayPalVaultTokenResponse>();
        int page = 1;
        while (true)
        {
            var req = await BuildRequestAsync(HttpMethod.Get,
                $"/v3/vault/payment-tokens?customer_id={Uri.EscapeDataString(customerId)}&page_size=20&page={page}",
                null, ct);
            var response = await SendAsync<PayPalListVaultTokensResponse>(req, ct);
            if (response.PaymentTokens != null)
                result.AddRange(response.PaymentTokens);
            if (page >= response.TotalPages || response.TotalPages == 0)
                break;
            page++;
        }
        return result;
    }

    public async Task DeleteVaultPaymentTokenAsync(string tokenId, CancellationToken ct = default)
    {
        var req = await BuildRequestAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{tokenId}", null, ct);
        using var client = _httpClientFactory.CreateClient();
        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.NoContent)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new PayPalException($"PayPal delete vault token failed ({(int)resp.StatusCode})", body, (int)resp.StatusCode);
        }
    }

    // ── Transaction Search API ────────────────────────────────────────────────

    public async Task<List<PayPalTransactionDetail>> SearchTransactionsAllPagesAsync(DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default)
    {
        var all = new List<PayPalTransactionDetail>();
        // PayPal limits search to 31-day windows; chunk if needed
        var chunkStart = startDate;
        while (chunkStart < endDate)
        {
            var chunkEnd = chunkStart.AddDays(31) < endDate ? chunkStart.AddDays(31) : endDate;
            int page = 1;
            while (true)
            {
                var startStr = chunkStart.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
                var endStr = chunkEnd.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
                var url = $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(startStr)}&end_date={Uri.EscapeDataString(endStr)}&page_size=500&page={page}";
                var req = await BuildRequestAsync(HttpMethod.Get, url, null, ct);
                var response = await SendAsync<PayPalTransactionSearchResponse>(req, ct);
                if (response.TransactionDetails != null)
                    all.AddRange(response.TransactionDetails);
                if (page >= response.TotalPages || response.TotalPages == 0)
                    break;
                page++;
            }
            chunkStart = chunkEnd;
        }
        return all;
    }
}

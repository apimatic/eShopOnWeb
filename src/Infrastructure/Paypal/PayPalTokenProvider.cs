using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Paypal;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Paypal;

/// <summary>
/// Obtains and caches a PayPal OAuth2 access token (client-credentials flow). Registered as a
/// singleton so the token is reused across requests and refreshed proactively before it expires.
/// </summary>
public class PayPalTokenProvider
{
    public const string HttpClientName = "paypal";

    private readonly IHttpClientFactory _httpFactory;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalTokenProvider> _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PayPalTokenProvider(IHttpClientFactory httpFactory, PayPalOptions options,
        ILogger<PayPalTokenProvider> logger)
    {
        _httpFactory = httpFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _expiresAt)
            return _accessToken;

        await _lock.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _expiresAt)
                return _accessToken;

            var http = _httpFactory.CreateClient(HttpClientName);
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials"
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

            using var response = await http.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var debugId = response.Headers.TryGetValues("PayPal-Debug-Id", out var ids) ? string.Join(",", ids) : null;
                _logger.LogError("PayPal token request failed: {Status} (debug_id={DebugId}).", (int)response.StatusCode, debugId);
                throw new PayPalApiException($"Failed to obtain PayPal access token ({(int)response.StatusCode}).",
                    (int)response.StatusCode, debugId, "TOKEN_REQUEST_FAILED");
            }

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            _accessToken = root.GetProperty("access_token").GetString();
            var expiresIn = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var secs) ? secs : 3600;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn - 60));
            _logger.LogInformation("Obtained PayPal access token (expires in {ExpiresIn}s).", expiresIn);
            return _accessToken!;
        }
        finally
        {
            _lock.Release();
        }
    }
}

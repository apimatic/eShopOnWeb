using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Obtains and caches an OAuth2 access token using the client-credentials flow defined by the
/// PayPal specs' security scheme (tokenUrl <c>/v1/oauth2/token</c>). The token is cached until
/// shortly before it expires and refreshed under a lock so concurrent callers share one token.
/// </summary>
public class PayPalAccessTokenProvider
{
    public const string HttpClientName = "PayPal";
    private const string TokenPath = "/v1/oauth2/token";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalAccessTokenProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PayPalAccessTokenProvider(IHttpClientFactory httpClientFactory, PayPalSettings settings,
        ILogger<PayPalAccessTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt)
        {
            return _cachedToken;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt)
            {
                return _cachedToken;
            }

            return await RequestNewTokenAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Forces the next call to fetch a fresh token (used after a 401).</summary>
    public void Invalidate() => _expiresAt = DateTimeOffset.MinValue;

    private async Task<string> RequestNewTokenAsync(CancellationToken cancellationToken)
    {
        // Fail with a clear message if the integration is invoked without being configured.
        _settings.Validate();

        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, _settings.ResolveBaseUrl() + TokenPath);
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Never log credentials or the Authorization header.
            _logger.LogError("PayPal token request failed with status {Status}.", (int)response.StatusCode);
            throw new PayPalApiException(response.StatusCode, "authentication_failure",
                "Failed to obtain a PayPal access token. Verify PayPal:ClientId / PayPal:ClientSecret.", null);
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var token = root.GetProperty("access_token").GetString()!;
        var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3000;

        _cachedToken = token;
        // Refresh a minute early to avoid using a token that expires mid-request.
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
        _logger.LogInformation("Obtained a new PayPal access token (valid ~{Seconds}s).", expiresIn);
        return token;
    }
}

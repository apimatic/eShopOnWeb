using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Obtains and caches a PayPal OAuth2 access token (client-credentials flow, token URL /v1/oauth2/token).</summary>
public interface IPayPalTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    void Invalidate();
}

public class PayPalTokenProvider : IPayPalTokenProvider
{
    /// <summary>Named HttpClient used for the token request (base address set in DI).</summary>
    public const string HttpClientName = "PayPal";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalTokenProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PayPalTokenProvider(IHttpClientFactory httpClientFactory, PayPalOptions options, ILogger<PayPalTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public void Invalidate()
    {
        _accessToken = null;
        _expiresAt = DateTimeOffset.MinValue;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (IsValid())
        {
            return _accessToken!;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (IsValid())
            {
                return _accessToken!;
            }
            return await FetchTokenAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool IsValid() => _accessToken is not null && DateTimeOffset.UtcNow < _expiresAt;

    private async Task<string> FetchTokenAsync(CancellationToken cancellationToken)
    {
        _options.Validate();
        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Never log the credentials; surface PayPal's error so a misconfiguration is diagnosable.
            throw PayPalErrorParser.ToException((int)response.StatusCode, body);
        }

        var token = JsonSerializer.Deserialize<TokenResponse>(body, PayPalJson.Options)
                    ?? throw new InvalidOperationException("PayPal returned an empty OAuth token response.");
        if (string.IsNullOrEmpty(token.AccessToken))
        {
            throw new InvalidOperationException("PayPal returned an OAuth response without an access token.");
        }

        _accessToken = token.AccessToken;
        // Refresh a minute early so a token never expires mid-request.
        var lifetime = Math.Max(token.ExpiresIn - 60, 30);
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(lifetime);
        _logger.LogInformation($"Obtained a PayPal access token (valid ~{token.ExpiresIn}s).");
        return _accessToken;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}

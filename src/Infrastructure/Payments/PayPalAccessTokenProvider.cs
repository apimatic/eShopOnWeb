using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Obtains and caches a PayPal OAuth2 access token using the client-credentials flow defined
/// by the specs' security scheme (tokenUrl <c>/v1/oauth2/token</c>). Registered as a singleton
/// so the token is shared and reused until shortly before it expires.
/// </summary>
public interface IPayPalAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

public sealed class PayPalAccessTokenProvider : IPayPalAccessTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalSettings _settings;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PayPalAccessTokenProvider(IHttpClientFactory httpClientFactory, IOptions<PayPalSettings> settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: a still-valid cached token.
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt)
        {
            return _cachedToken;
        }

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt)
            {
                return _cachedToken;
            }

            var token = await RequestTokenAsync(cancellationToken);
            return token;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<string> RequestTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new PayPalApiException("PayPal credentials are not configured (PayPal:ClientId / PayPal:ClientSecret).");
        }

        var client = _httpClientFactory.CreateClient(PayPalHttpClient.Name);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new PayPalApiException("Failed to reach PayPal to obtain an access token.", ex);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Never include the request (which carries the client secret) in the error.
            throw new PayPalApiException($"PayPal token request failed with status {(int)response.StatusCode}.");
        }

        var token = JsonSerializer.Deserialize<OAuthTokenResponse>(body);
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
        {
            throw new PayPalApiException("PayPal token response did not contain an access token.");
        }

        _cachedToken = token.AccessToken;
        // Refresh a minute early to avoid using a token that expires mid-request.
        var lifetime = Math.Max(token.ExpiresIn - 60, 30);
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(lifetime);
        return _cachedToken;
    }
}

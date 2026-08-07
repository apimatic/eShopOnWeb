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

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

public interface IPayPalAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Obtains and caches PayPal OAuth2 client-credentials access tokens. A single token is shared across
/// requests and refreshed shortly before it expires. Thread-safe.
/// </summary>
public class PayPalAccessTokenProvider : IPayPalAccessTokenProvider
{
    // Refresh a bit before the real expiry to avoid using a token that expires mid-request.
    private static readonly TimeSpan ExpiryLeeway = TimeSpan.FromSeconds(60);

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

            var (token, expiresIn) = await RequestTokenAsync(cancellationToken);
            _cachedToken = token;
            _expiresAt = DateTimeOffset.UtcNow.Add(expiresIn).Subtract(ExpiryLeeway);
            return token;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<(string token, TimeSpan expiresIn)> RequestTokenAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(PayPalHttpClient.Name);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Do not surface PayPal's raw error body (may echo request specifics); keep it generic.
            throw new PaymentGatewayException(
                $"Failed to authenticate with PayPal (HTTP {(int)response.StatusCode}).");
        }

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var accessToken = root.GetProperty("access_token").GetString()
            ?? throw new PaymentGatewayException("PayPal did not return an access token.");
        var expiresInSeconds = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 300;

        return (accessToken, TimeSpan.FromSeconds(expiresInSeconds));
    }
}

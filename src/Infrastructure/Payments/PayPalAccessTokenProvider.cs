using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Acquires and caches a PayPal OAuth2 access token via the client-credentials flow declared by the
/// specs' <c>Oauth2</c> security scheme (<c>flows.clientCredentials.tokenUrl = /v1/oauth2/token</c>).
/// The token request/response envelope is the OAuth2 standard the scheme references (PayPal docs
/// confirm <c>Basic</c> auth with <c>grant_type=client_credentials</c> and an <c>access_token</c> /
/// <c>expires_in</c> response); the spec itself contributes the endpoint and the scheme.
/// </summary>
public interface IPayPalAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

public sealed class PayPalAccessTokenProvider : IPayPalAccessTokenProvider, IDisposable
{
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalAccessTokenProvider> _logger;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PayPalAccessTokenProvider(IOptions<PayPalSettings> settings, ILogger<PayPalAccessTokenProvider> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _httpClient = new HttpClient { BaseAddress = new Uri(_settings.ResolveBaseUrl() + "/") };
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt)
        {
            return _cachedToken;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt)
            {
                return _cachedToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal token request failed with status {Status}.", (int)response.StatusCode);
                throw new PayPalApiException(
                    "Failed to obtain a PayPal access token. Check the configured PayPal credentials.",
                    (int)response.StatusCode, TryReadErrorName(body), body);
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var accessToken = root.GetProperty("access_token").GetString()
                ?? throw new PayPalApiException("PayPal token response had no access_token.",
                    (int)response.StatusCode, null, body);
            var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 300;

            _cachedToken = accessToken;
            // Refresh a minute before real expiry to avoid using a token that expires mid-flight.
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            _logger.LogInformation("Obtained a PayPal access token (expires in {ExpiresIn}s).", expiresIn);
            return accessToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string? TryReadErrorName(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _lock.Dispose();
    }
}

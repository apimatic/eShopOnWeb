using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Caches the PayPal OAuth client-credentials token process-wide and refreshes it shortly
/// before expiry. Registered as a singleton so all gateway instances share one token.
/// </summary>
public class PayPalAccessTokenProvider
{
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(2);

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalAccessTokenProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PayPalAccessTokenProvider(HttpClient httpClient, IOptions<PayPalSettings> settings,
        ILogger<PayPalAccessTokenProvider> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _expiresAt - ExpiryMargin)
        {
            return _accessToken;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _expiresAt - ExpiryMargin)
            {
                return _accessToken;
            }

            PayPalGatewayGuard.EnsureConfigured(_settings);

            var baseUrl = _settings.ResolveBaseUrl();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal token request failed with status {StatusCode}", (int)response.StatusCode);
                throw new PayPalGatewayException(response.StatusCode, null,
                    $"PayPal token request failed with status {(int)response.StatusCode}.");
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(body);
            if (string.IsNullOrEmpty(token?.AccessToken))
            {
                throw new PayPalGatewayException(response.StatusCode, null,
                    "PayPal token request succeeded but returned no access token.");
            }

            _accessToken = token.AccessToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
            return _accessToken;
        }
        finally
        {
            _lock.Release();
        }
    }
}

internal static class PayPalGatewayGuard
{
    public static void EnsureConfigured(PayPalSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set the PayPal:ClientId and PayPal:ClientSecret " +
                "configuration keys (e.g. from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment " +
                "variables via user-secrets).");
        }
    }
}

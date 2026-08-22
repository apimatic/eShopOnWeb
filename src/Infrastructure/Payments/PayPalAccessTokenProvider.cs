using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalAccessTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<PayPalOptions> _options;
    private readonly ILogger<PayPalAccessTokenProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiresAt;

    public PayPalAccessTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<PayPalOptions> options,
        ILogger<PayPalAccessTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (HasValidToken())
        {
            return _accessToken!;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (HasValidToken())
            {
                return _accessToken!;
            }

            var options = _options.Value;
            if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
            {
                throw new PayPalApiException("PayPal ClientId and ClientSecret are not configured.");
            }

            var client = _httpClientFactory.CreateClient("PayPal");
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ClientId}:{options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = TryDeserializeError(body);
                _logger.LogWarning(
                    "PayPal token request failed with {StatusCode}. debug_id={DebugId}",
                    (int)response.StatusCode,
                    error?.DebugId);
                throw new PayPalApiException(
                    error?.Message ?? "Failed to obtain a PayPal access token.",
                    error?.DebugId,
                    response.StatusCode);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponseDto>(body, PayPalJson.Options);
            if (token?.AccessToken == null)
            {
                throw new PayPalApiException("PayPal token response did not include an access_token.");
            }

            _accessToken = token.AccessToken;
            var lifetime = token.ExpiresIn > 60 ? token.ExpiresIn - 60 : Math.Max(token.ExpiresIn, 1);
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(lifetime);
            return _accessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        _accessToken = null;
        _expiresAt = DateTimeOffset.MinValue;
    }

    private bool HasValidToken() =>
        !string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _expiresAt;

    private static PayPalErrorDto? TryDeserializeError(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<PayPalErrorDto>(body, PayPalJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Gets and caches the OAuth 2.0 access token that every PayPal REST call is authenticated with
/// (client credentials grant against <c>POST /v1/oauth2/token</c>). Tokens are valid for roughly nine
/// hours; each response's own <c>expires_in</c> is honoured so a token is never used past its life.
/// </summary>
public class PayPalAccessTokenProvider
{
    public const string HTTP_CLIENT_NAME = "paypal";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<PayPalSettings> _settings;
    private readonly IAppLogger<PayPalAccessTokenProvider> _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Registered as a single instance so the token is fetched once and reused across requests. The
    /// logger is built from the (singleton) factory rather than injected as a scoped service.
    /// </summary>
    public PayPalAccessTokenProvider(IHttpClientFactory httpClientFactory, IOptions<PayPalSettings> settings,
        Microsoft.Extensions.Logging.ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = new Logging.LoggerAdapter<PayPalAccessTokenProvider>(loggerFactory);
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!IsExpired(_token, _expiresAt))
        {
            return _token!;
        }

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsExpired(_token, _expiresAt))
            {
                return _token!;
            }

            var config = _settings.Value;
            if (!config.IsConfigured)
            {
                throw new PaymentProcessorException($"The payment processor is not configured: {config.Problem}");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{config.BaseAddress}/v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials"
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.ClientId}:{config.ClientSecret}")));

            using var client = _httpClientFactory.CreateClient(HTTP_CLIENT_NAME);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // The body is deliberately not logged: this request carries the app's credentials.
                _logger.LogError($"PayPal token request failed with HTTP {(int)response.StatusCode}.");
                throw new PaymentProcessorException("Could not authenticate with the payment processor.",
                    httpStatus: (int)response.StatusCode);
            }

            using var document = JsonDocument.Parse(body);
            var token = document.RootElement.TryGetProperty("access_token", out var accessToken)
                ? accessToken.GetString()
                : null;
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiry) &&
                            expiry.TryGetInt64(out var seconds)
                ? seconds
                : 300;

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new PaymentProcessorException("The payment processor returned no access token.");
            }

            // Renew well inside the reported lifetime so a request never races the token expiring.
            _token = token;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 120));
            _logger.LogInformation($"Authenticated with PayPal; the token holds until {_expiresAt:O}.");
            return token;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError($"Could not reach the payment processor to authenticate: {exception.Message}");
            throw new PaymentProcessorException("The payment processor could not be reached. Try again shortly.");
        }
        finally
        {
            _mutex.Release();
        }
    }

    private static bool IsExpired(string? token, DateTimeOffset expiresAt)
        => string.IsNullOrEmpty(token) || DateTimeOffset.UtcNow >= expiresAt;
}

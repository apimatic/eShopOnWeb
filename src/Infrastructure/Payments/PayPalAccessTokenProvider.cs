using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalAccessTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _refreshAt;

    public PayPalAccessTokenProvider(IHttpClientFactory httpClientFactory, IOptions<PayPalOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<string> GetAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _accessToken != null && DateTimeOffset.UtcNow < _refreshAt) return _accessToken;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _accessToken != null && DateTimeOffset.UtcNow < _refreshAt) return _accessToken;
            _options.EnsureValid();

            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_options.ResolveBaseUrl().TrimEnd('/')}/v1/oauth2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _httpClientFactory.CreateClient("PayPal").SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"PayPal OAuth failed with HTTP {(int)response.StatusCode}.");

            using var document = JsonDocument.Parse(json);
            _accessToken = document.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("PayPal OAuth response did not contain an access token.");
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiry)
                ? expiry.GetInt32()
                : 300;
            _refreshAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            return _accessToken;
        }
        finally
        {
            _gate.Release();
        }
    }
}

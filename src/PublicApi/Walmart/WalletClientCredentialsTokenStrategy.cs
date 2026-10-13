using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WalmartApIs.Core.Authentication.OAuth2;
using WalmartApIs.Core.Authentication.OAuth2.ClientCredentials;

namespace Microsoft.eShopWeb.PublicApi.Walmart;

/// <summary>
/// Token-acquisition strategy for the SDK's <c>WalletAuth</c> OAuth2 client-credentials scheme.
/// It mirrors the SDK's built-in behaviour (HTTP Basic token request,
/// <c>grant_type=client_credentials</c>) but resolves the token endpoint through the
/// <c>Walmart:BaseUrl</c> override, so obtaining access honours the same host override as every
/// other Walmart call. Token caching is still handled by the SDK's OAuth2 scheme around this
/// strategy.
/// </summary>
public sealed class WalletClientCredentialsTokenStrategy : IOAuth2TokenStrategy<OAuth2ClientCredentials>
{
    /// <summary>The contract's WalletAuth token endpoint (scheme/host/port replaced when overridden).</summary>
    public const string DefaultTokenUrl = "https://marketplace.walmart.com/seller/v1/auth/token";

    private readonly HttpClient _httpClient;
    private readonly string _tokenUrl;

    public WalletClientCredentialsTokenStrategy(HttpClient httpClient, string? baseUrlOverride)
    {
        _httpClient = httpClient;
        _tokenUrl = WalmartBaseUrlOverride.Apply(DefaultTokenUrl, baseUrlOverride);
    }

    public async Task<OAuthToken> GetToken(OAuth2ClientCredentials credentials, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _tokenUrl);

        var basic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{credentials.ClientId}:{credentials.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"),
        };
        if (!string.IsNullOrEmpty(credentials.Scope))
        {
            form.Add(new("scope", credentials.Scope!));
        }
        request.Content = new FormUrlEncodedContent(form);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var token = JsonSerializer.Deserialize<OAuthToken>(payload);
        if (token is null)
        {
            throw new InvalidOperationException("Walmart token endpoint returned an empty token response.");
        }

        return token;
    }
}

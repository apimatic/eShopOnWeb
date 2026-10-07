using System;
using System.Threading;
using System.Threading.Tasks;
using BoxPlatformApi.Core.Authentication.OAuth2;
using BoxPlatformApi.Core.Authentication.OAuth2.AuthorizationCode;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.DigitalFiles.Box;

/// <summary>
/// Hands the SDK the access token from <c>Box:AccessToken</c> instead of running the interactive
/// authorization-code grant. The token is re-read from configuration whenever the SDK's cached copy
/// expires (<see cref="BoxOptions.TokenCacheSeconds"/>) or a call comes back 401, so a rotated token
/// takes effect without a restart.
/// </summary>
public sealed class BoxConfiguredTokenStrategy : IOAuth2RefreshableTokenStrategy<OAuth2AuthorizationCodeCredentials>
{
    private readonly IOptionsMonitor<BoxOptions> _options;

    public BoxConfiguredTokenStrategy(IOptionsMonitor<BoxOptions> options)
    {
        _options = options;
    }

    public Task<OAuthTokenRefreshable> GetToken(OAuth2AuthorizationCodeCredentials credentials, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.AccessToken))
            throw new InvalidOperationException("Box:AccessToken is not configured.");

        return Task.FromResult(new OAuthTokenRefreshable
        {
            AccessToken = options.AccessToken.Trim(),
            TokenType = "bearer",
            ExpiresIn = options.TokenCacheSeconds,
        });
    }

    // There is no refresh token: returning null makes the SDK call GetToken, which re-reads configuration.
    public Task<OAuthTokenRefreshable?> TryRefreshToken(OAuth2AuthorizationCodeCredentials credentials, string refreshToken, CancellationToken cancellationToken) =>
        Task.FromResult<OAuthTokenRefreshable?>(null);
}

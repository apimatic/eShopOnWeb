using System;
using System.Threading;
using System.Threading.Tasks;
using BoxPlatformApi.Core.Authentication.OAuth2;
using BoxPlatformApi.Core.Authentication.OAuth2.AuthorizationCode;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.DigitalFiles.Box;

/// <summary>
/// Supplies the SDK with the pre-issued access token from <c>Box:AccessToken</c> instead of running the
/// interactive OAuth authorization-code flow (which cannot run in a server).
/// The token is re-read from configuration roughly every <see cref="ReReadAfterSeconds"/>/2 seconds,
/// so a rotated token is picked up without restarting the host.
/// </summary>
public sealed class BoxAccessTokenStrategy : IOAuth2RefreshableTokenStrategy<OAuth2AuthorizationCodeCredentials>
{
    private const int ReReadAfterSeconds = 60;

    private readonly IOptionsMonitor<BoxOptions> _options;

    public BoxAccessTokenStrategy(IOptionsMonitor<BoxOptions> options)
    {
        _options = options;
    }

    /// <summary>
    /// Credentials the SDK requires to be non-null before it applies any token. None of these values are
    /// sent to Box: <see cref="BoxAccessTokenStrategy"/> replaces the authorization-code exchange.
    /// </summary>
    public static OAuth2AuthorizationCodeCredentials PlaceholderCredentials { get; } = new()
    {
        ClientId = "eshop-preissued-access-token",
        RedirectUri = "urn:ietf:wg:oauth:2.0:oob",
        PromptForAuthorizationCode = (_, _) => throw new InvalidOperationException(
            "Interactive Box authorization is not supported; configure Box:AccessToken."),
    };

    public Task<OAuthTokenRefreshable> GetToken(OAuth2AuthorizationCodeCredentials credentials, CancellationToken cancellationToken)
    {
        var accessToken = _options.CurrentValue.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Box:AccessToken is not configured.");
        }

        return Task.FromResult(new OAuthTokenRefreshable
        {
            AccessToken = accessToken,
            TokenType = "bearer",
            ExpiresIn = ReReadAfterSeconds,
        });
    }

    // There is no refresh token; returning null makes the SDK call GetToken again, which re-reads configuration.
    public Task<OAuthTokenRefreshable?> TryRefreshToken(OAuth2AuthorizationCodeCredentials credentials, string refreshToken,
        CancellationToken cancellationToken) =>
        Task.FromResult<OAuthTokenRefreshable?>(null);
}

using Square.Core.Authentication.OAuth2;
using Square.Core.Authentication.OAuth2.AuthorizationCode;

namespace SquareCheck.SignIn;

internal sealed class PreObtainedTokenStrategy
    : IOAuth2RefreshableTokenStrategy<OAuth2AuthorizationCodeCredentials>
{
    private readonly OAuthTokenRefreshable _token;

    internal PreObtainedTokenStrategy(string accessToken)
    {
        _token = new OAuthTokenRefreshable
        {
            AccessToken = accessToken,
            TokenType = "bearer",
        };
    }

    public Task<OAuthTokenRefreshable> GetToken(
        OAuth2AuthorizationCodeCredentials credentials, CancellationToken cancellationToken)
        => Task.FromResult(_token);

    public Task<OAuthTokenRefreshable?> TryRefreshToken(
        OAuth2AuthorizationCodeCredentials credentials,
        string refreshToken,
        CancellationToken cancellationToken)
        => Task.FromResult<OAuthTokenRefreshable?>(null);
}

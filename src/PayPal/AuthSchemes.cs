using PayPal.Core;
using PayPal.Core.Authentication;
using PayPal.Core.Authentication.OAuth2;
using PayPal.Core.Authentication.OAuth2.ClientCredentials;

namespace PayPal;

internal sealed class AuthSchemes
{
    public IAuthScheme Oauth2 { get; }

    public AuthSchemes(PayPalClientOptions options, Server server, RawClient rawClient)
    {
        Oauth2 =
            OAuth2Scheme<OAuth2ClientCredentials>.Create(options.Oauth2,
                options.Oauth2TokenStrategy ??
                    OAuth2ClientCredentialsStrategy.ForBasicAuthRequest(server.Default("/v1/oauth2/token"), rawClient));
    }
}

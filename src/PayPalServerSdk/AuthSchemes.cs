using PayPalServerSdk.Core;
using PayPalServerSdk.Core.Authentication;
using PayPalServerSdk.Core.Authentication.OAuth2;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;

namespace PayPalServerSdk;

internal sealed class AuthSchemes
{
    public IAuthScheme Oauth2 { get; }

    public AuthSchemes(PayPalServerSdkClientOptions options, Server server, RawClient rawClient)
    {
        Oauth2 =
            OAuth2Scheme<OAuth2ClientCredentials>.Create(options.Oauth2,
                options.Oauth2TokenStrategy ??
                    OAuth2ClientCredentialsStrategy.ForBasicAuthRequest(server.Default("/v1/oauth2/token"), rawClient));
    }
}

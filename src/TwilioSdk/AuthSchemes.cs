using TwilioSdk.Core.Authentication;
using TwilioSdk.Core.Authentication.Basic;

namespace TwilioSdk;

internal sealed class AuthSchemes
{
    public IAuthScheme AccountSidAuthToken { get; }

    public AuthSchemes(TwilioSdkClientOptions options)
    {
        AccountSidAuthToken = BasicAuthScheme.Create(options.AccountSidAuthToken);
    }
}

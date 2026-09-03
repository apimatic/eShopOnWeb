using Twilio.Core.Authentication;
using Twilio.Core.Authentication.Basic;

namespace Twilio;

internal sealed class AuthSchemes
{
    public IAuthScheme AccountSidAuthToken { get; }

    public AuthSchemes(TwilioClientOptions options)
    {
        AccountSidAuthToken = BasicAuthScheme.Create(options.AccountSidAuthToken);
    }
}

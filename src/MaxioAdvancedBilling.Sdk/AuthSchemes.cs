using MaxioAdvancedBilling.Core.Authentication;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Authentication.Bearer;

namespace MaxioAdvancedBilling;

internal sealed class AuthSchemes
{
    public IAuthScheme BasicAuth { get; }
    public IAuthScheme BearerAuth { get; }

    public AuthSchemes(MaxioAdvancedBillingClientOptions options)
    {
        BasicAuth = BasicAuthScheme.Create(options.BasicAuth);
        BearerAuth = BearerAuthScheme.Create(options.BearerAuth);
    }
}

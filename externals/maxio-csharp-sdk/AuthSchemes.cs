using Maxio.Core.Authentication;
using Maxio.Core.Authentication.Basic;
using Maxio.Core.Authentication.Bearer;

namespace Maxio;

internal sealed class AuthSchemes
{
    public IAuthScheme BasicAuth { get; }
    public IAuthScheme BearerAuth { get; }

    public AuthSchemes(MaxioClientOptions options)
    {
        BasicAuth = BasicAuthScheme.Create(options.BasicAuth);
        BearerAuth = BearerAuthScheme.Create(options.BearerAuth);
    }
}

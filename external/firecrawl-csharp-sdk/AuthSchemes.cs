using FirecrawlApi.Core.Authentication;
using FirecrawlApi.Core.Authentication.Bearer;

namespace FirecrawlApi;

internal sealed class AuthSchemes
{
    public IAuthScheme BearerAuth { get; }

    public AuthSchemes(FirecrawlApiClientOptions options)
    {
        BearerAuth = BearerAuthScheme.Create(options.BearerAuth);
    }
}

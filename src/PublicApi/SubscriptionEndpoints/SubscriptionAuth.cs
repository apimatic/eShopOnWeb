using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Authorization metadata for the subscription endpoints. The scheme is pinned to JWT bearer
/// explicitly: this host also registers Identity cookie authentication as the default challenge
/// scheme, so relying on the default would redirect API callers to a login page (302) instead of
/// validating the bearer token.
/// </summary>
internal static class SubscriptionAuth
{
    public static readonly IAuthorizeData[] JwtPolicy =
    {
        new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme }
    };
}

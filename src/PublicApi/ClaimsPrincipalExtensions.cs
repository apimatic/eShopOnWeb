using System.Security;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's identity (user name), used as the buyer id so every shopper-scoped endpoint
    /// acts only on the caller's own data. The value comes from the validated JWT.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name;
        if (string.IsNullOrEmpty(name))
        {
            throw new SecurityException("The authenticated caller has no identity name claim.");
        }
        return name;
    }
}

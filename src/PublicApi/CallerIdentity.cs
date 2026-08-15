using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Reads the caller's identity from the validated JWT. The buyer id is the token's name claim,
/// matching how the rest of eShopOnWeb keys orders and buyers to a user. Every shopper-scoped
/// endpoint acts only on the data owned by this identity.
/// </summary>
public static class CallerIdentity
{
    public static string GetBuyerId(this HttpContext httpContext)
    {
        var name = httpContext.User.Identity?.Name
                   ?? httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(name))
        {
            // Should not happen behind [Authorize]; treated as unauthenticated if it does.
            throw new System.UnauthorizedAccessException("The request is not associated with an authenticated user.");
        }
        return name;
    }
}

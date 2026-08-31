using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class IdentityExtensions
{
    /// <summary>
    /// The caller's identity from the JWT (the authenticate endpoint puts the username
    /// in the name claim). Null when unauthenticated.
    /// </summary>
    public static string? GetCallerId(this ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;
    }
}

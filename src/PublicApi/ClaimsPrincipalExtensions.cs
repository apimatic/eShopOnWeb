using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The caller's identity, taken from the JWT. Null when unauthenticated.</summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) => user.Identity?.Name;
}

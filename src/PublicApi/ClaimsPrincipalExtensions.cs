using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The buyer identity for the authenticated caller. eShopOnWeb scopes orders and saved cards to
    /// the user's identity name (the value carried in the JWT's name claim).
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal principal)
        => principal.FindFirstValue(ClaimTypes.Name) ?? principal.Identity?.Name;
}

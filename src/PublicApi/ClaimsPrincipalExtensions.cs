using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's identity (username) taken from the token. This is the same value used as an
    /// order's BuyerId and a contact number's / notification's owner.
    /// </summary>
    public static string? GetUserId(this ClaimsPrincipal principal)
    {
        return principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
    }
}

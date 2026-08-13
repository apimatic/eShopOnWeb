using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Resolves the caller's shopper identity from the validated JWT. The token carries the user name as
/// its name claim; this is the same value used as an order's <c>BuyerId</c>, so every shopper-scoped
/// endpoint acts only on the caller's own data.
/// </summary>
public static class CallerIdentity
{
    public static string? GetUserName(ClaimsPrincipal principal)
    {
        return principal.Identity?.Name
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("unique_name")
            ?? principal.FindFirstValue("sub");
    }
}

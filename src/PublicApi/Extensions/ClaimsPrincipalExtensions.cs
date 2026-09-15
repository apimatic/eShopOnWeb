using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's buyer identity, taken from the JWT (the token carries the username in the Name claim).
    /// Every shopper-scoped endpoint uses this so a caller can only ever act on their own data.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.Name)
            ?? user.Identity?.Name
            ?? string.Empty;
    }
}

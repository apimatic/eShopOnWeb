using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The buyer identity for the calling shopper, taken from the JWT (the username / name claim).
    /// Every shopper-scoped endpoint keys its data off this so callers only ever see their own data.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrEmpty(name) ? string.Empty : name;
    }
}

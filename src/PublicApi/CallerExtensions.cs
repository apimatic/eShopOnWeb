using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class CallerExtensions
{
    /// <summary>
    /// The signed-in caller's identity (used as the buyer id), taken from the JWT — never from the request body,
    /// so a caller can only ever act on their own data. Returns null when unauthenticated.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.FindFirstValue(ClaimTypes.Name);
        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }
        return user.Identity?.Name;
    }
}

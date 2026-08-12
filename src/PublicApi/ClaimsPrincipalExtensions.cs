using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's buyer identity, taken from the JWT. This matches how the rest of the app identifies a
    /// buyer (the user name), so orders and contact numbers scope to the same value.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
}

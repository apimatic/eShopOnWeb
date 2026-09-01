using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The buyer identity carried by the JWT (the user's name). Falls back to the raw
    /// "unique_name" claim in case inbound claim mapping is disabled.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue("unique_name");
}

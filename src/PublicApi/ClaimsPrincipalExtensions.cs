using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The identity used to key a shopper's orders and saved cards. It comes from the JWT (the name
    /// claim), never from the request body, so a caller can only ever act on their own data.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user)
    {
        return user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue("unique_name")
            ?? user.FindFirstValue("name");
    }
}

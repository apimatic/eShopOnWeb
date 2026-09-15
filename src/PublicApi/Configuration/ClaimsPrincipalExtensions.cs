using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The buyer identity carried by the JWT — the same value used as Order.BuyerId elsewhere in the
    /// app (the user name / email). Every shopper-scoped action acts only on this caller's own data.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.Name)
            ?? user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? string.Empty;
    }
}

using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's buyer identity, taken from the token. eShop uses the username (name claim) as the
    /// BuyerId throughout, so orders and contact numbers registered here line up with the rest of the app.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        return name ?? string.Empty;
    }
}

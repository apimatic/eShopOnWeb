using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's identity as carried by the JWT (the ClaimTypes.Name claim), which is also the
    /// value used as an order's BuyerId. Returns null when no identity is present.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) => user.Identity?.Name;
}

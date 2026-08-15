using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Reads the caller's shopper identity (the buyer id) from the JWT.</summary>
public static class CallerIdentity
{
    /// <summary>
    /// The buyer id is the token's Name claim — the same value the storefront uses as an order's BuyerId.
    /// Returns null when unauthenticated.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
}

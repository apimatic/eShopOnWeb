using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

internal static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The buyer identity carried by the JWT — the token's name claim, consistent with how the
    /// storefront sets <c>Order.BuyerId</c>. Returns null when the caller is unauthenticated.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
}

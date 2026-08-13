using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's identity as used for order/contact ownership. The token stores the username under
    /// <see cref="ClaimTypes.Name"/>, and an order's BuyerId is that username.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
}

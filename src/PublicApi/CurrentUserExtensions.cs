using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class CurrentUserExtensions
{
    /// <summary>
    /// The calling shopper's id — the username carried on their JWT (ClaimTypes.Name),
    /// matching the convention used for <c>Order.BuyerId</c> and saved cards.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
        => user.Identity?.Name ?? string.Empty;
}

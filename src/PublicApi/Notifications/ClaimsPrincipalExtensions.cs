using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The buyer scope key for the signed-in caller — their user name, which is what the app uses as
    /// <c>Order.BuyerId</c>. Returns null when the token carries no name claim.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;

    /// <summary>True when the caller holds the administrator (operator) role.</summary>
    public static bool IsOperator(this ClaimsPrincipal user)
        => user.IsInRole(Constants.Roles.ADMINISTRATORS);
}

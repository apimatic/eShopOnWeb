using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

internal static class CallerExtensions
{
    /// <summary>The signed-in shopper's identity (username), taken from the JWT. This is the value
    /// <c>Order.BuyerId</c> and <c>ContactNumber.BuyerId</c> are keyed by.</summary>
    public static string? UserName(this ClaimsPrincipal user) => user.Identity?.Name;
}

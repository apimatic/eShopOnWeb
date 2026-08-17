using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

/// <summary>
/// Reads the caller's identity from the validated JWT. The buyer id is the username carried in the
/// <see cref="ClaimTypes.Name"/> claim — the same value eShop uses as <c>Order.BuyerId</c> — so orders and
/// contact numbers placed through the API are owned by, and visible only to, the authenticated shopper.
/// </summary>
public static class CallerIdentity
{
    public static string? GetBuyerId(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
}

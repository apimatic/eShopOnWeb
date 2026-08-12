using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.NotificationsFeature;

/// <summary>
/// Helpers for reading the signed-in caller's identity from the JWT. The buyer id used across
/// eShop (baskets, orders) is the username / email carried in <see cref="ClaimTypes.Name"/>.
/// </summary>
public static class CallerIdentity
{
    public static string? GetBuyerId(this ClaimsPrincipal? user)
    {
        if (user is null)
            return null;

        var name = user.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(name) ? user.Identity?.Name : name;
    }
}

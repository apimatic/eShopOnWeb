using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Resolves the caller's buyer identity from the validated JWT. The token carries the shopper's
/// username as its name claim, and that value is used as the buyer id throughout the payment flows.
/// </summary>
public static class BuyerIdAccessor
{
    public static string? GetBuyerId(ClaimsPrincipal? user)
    {
        if (user is null) return null;

        var name = user.Identity?.Name;
        if (!string.IsNullOrEmpty(name)) return name;

        return user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue("unique_name")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}

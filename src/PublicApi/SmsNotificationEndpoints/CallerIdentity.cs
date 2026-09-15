using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

/// <summary>
/// Helpers for reading the caller's identity from the JWT. The buyer id used across the app is the
/// username (ClaimTypes.Name), matching how the storefront stamps orders and baskets.
/// </summary>
public static class CallerIdentity
{
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
}

using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Reads the caller's identity from the validated JWT. The buyer identity always comes from the
/// token, never from the request body, so one shopper can never act as another.
/// </summary>
public static class CallerIdentity
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        return string.IsNullOrWhiteSpace(buyerId) ? string.Empty : buyerId;
    }

    public static bool IsOperator(this ClaimsPrincipal user) =>
        user.IsInRole(Constants.Roles.ADMINISTRATORS);
}

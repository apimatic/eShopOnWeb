using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Reads the caller's identity from the validated JWT. The caller identity is the token subject
/// (username), which is what eShop uses as an order's/bill's owner.
/// </summary>
internal static class CallerContext
{
    /// <summary>The caller's buyer id (username), or empty when the principal is unauthenticated.</summary>
    public static string BuyerId(this ClaimsPrincipal user) =>
        user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name) ?? string.Empty;

    /// <summary>Whether the caller acts as an operator (administrator) and may act on any bill.</summary>
    public static bool IsOperator(this ClaimsPrincipal user) =>
        user.IsInRole(Constants.Roles.ADMINISTRATORS);
}

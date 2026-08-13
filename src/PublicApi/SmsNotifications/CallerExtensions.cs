using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

/// <summary>
/// Helpers for reading the caller's identity and role from the validated JWT. The caller's identity
/// is the buyer identity used to scope shopper data — it always comes from the token, never the body.
/// </summary>
public static class CallerExtensions
{
    /// <summary>The buyer identity (JWT name/subject) of the caller, or null if unauthenticated.</summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;

    /// <summary>True when the caller holds the privileged administrator (operator) role.</summary>
    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user.IsInRole(Constants.Roles.ADMINISTRATORS);
}

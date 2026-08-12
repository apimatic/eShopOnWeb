using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Helpers for reading the authenticated caller's identity from the JWT. The buyer id used across
/// the app is the username/email carried in <see cref="ClaimTypes.Name"/>.
/// </summary>
public static class CurrentUser
{
    /// <summary>The caller's buyer id (username/email), or null if unauthenticated.</summary>
    public static string? GetBuyerId(this ClaimsPrincipal principal)
        => principal.FindFirstValue(ClaimTypes.Name);

    /// <summary>Whether the caller is in the administrators (operator) role.</summary>
    public static bool IsAdministrator(this ClaimsPrincipal principal)
        => principal.IsInRole(Constants.Roles.ADMINISTRATORS);
}

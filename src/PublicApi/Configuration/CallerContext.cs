using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Reads the caller's identity from the validated JWT. The caller's identity always comes from the
/// token — never from the request body — so shopper-scoped endpoints act only on the caller's data.
/// </summary>
public static class CallerContext
{
    /// <summary>The caller's user name (the buyer id used throughout the app), or empty if absent.</summary>
    public static string GetCallerId(this ClaimsPrincipal user) =>
        user?.Identity?.Name ?? string.Empty;

    /// <summary>True when the caller holds the administrator role used for operator actions.</summary>
    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user?.IsInRole(Constants.Roles.ADMINISTRATORS) ?? false;
}

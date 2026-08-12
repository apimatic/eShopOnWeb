using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Reads the caller's identity and role from the validated JWT.</summary>
internal static class CallerIdentity
{
    /// <summary>The caller's username (the buyer id used across baskets and orders), or null.</summary>
    public static string? GetUserName(ClaimsPrincipal user) => user.Identity?.Name;

    /// <summary>True when the caller holds the administrator (operator) role.</summary>
    public static bool IsAdministrator(ClaimsPrincipal user) => user.IsInRole(Constants.Roles.ADMINISTRATORS);
}

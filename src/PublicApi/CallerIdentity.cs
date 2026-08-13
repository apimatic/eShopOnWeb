using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Helpers for reading the caller's identity from the JWT the request was authenticated with. The buyer id used
/// throughout the order/contact model is the caller's user name (as carried in the token), so every shopper-scoped
/// endpoint acts only on the caller's own data.
/// </summary>
internal static class CallerIdentity
{
    /// <summary>The caller's user name (buyer id) from the token, or null if unauthenticated.</summary>
    public static string? UserName(this ClaimsPrincipal? user) => user?.Identity?.Name;

    /// <summary>Whether the caller holds the administrator (operator) role used for privileged endpoints.</summary>
    public static bool IsAdministrator(this ClaimsPrincipal? user) =>
        user?.IsInRole(Constants.Roles.ADMINISTRATORS) ?? false;
}

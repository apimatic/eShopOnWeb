using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Helpers for reading the caller's identity from the validated JWT. The caller's identity always
/// comes from the token, never from the request body.
/// </summary>
public static class CallerIdentity
{
    /// <summary>The buyer id used across the app: the authenticated user's name.</summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) => user?.Identity?.Name;

    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user is not null && user.IsInRole(Constants.Roles.ADMINISTRATORS);
}

using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Helpers for reading the caller's identity out of the JWT.</summary>
public static class Caller
{
    /// <summary>The signed-in shopper's username (the token's Name claim), or null if absent.</summary>
    public static string? UserName(this ClaimsPrincipal user) => user.Identity?.Name;

    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}

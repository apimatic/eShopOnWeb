using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.NotificationsApi;

/// <summary>
/// Helpers for reading the caller's identity from the validated JWT. The token carries
/// the user name as <see cref="ClaimTypes.Name"/>; that value is used as the buyer/owner
/// id so every shopper-scoped endpoint acts only on the caller's own data.
/// </summary>
public static class CallerContext
{
    public static string? GetUserId(this ClaimsPrincipal user) => user.Identity?.Name;

    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}

using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi;

internal static class AuthenticatedUser
{
    public static string? GetBuyerId(ClaimsPrincipal user) => user.Identity?.Name;

    public static bool IsAdministrator(ClaimsPrincipal user) =>
        user.IsInRole(Constants.Roles.ADMINISTRATORS);
}

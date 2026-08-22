using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

internal static class CallerIdentity
{
    public static string? BuyerId(ClaimsPrincipal user) => user.Identity?.Name;

    public static bool IsOperator(ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}

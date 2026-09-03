using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

internal static class BuyerIdentity
{
    public static string? GetBuyerId(this ClaimsPrincipal user) => user.Identity?.Name;

    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}

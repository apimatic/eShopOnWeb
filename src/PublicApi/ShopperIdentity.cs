using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class ShopperIdentity
{
    public static string? TryGetBuyerId(HttpContext httpContext)
        => httpContext.User.Identity?.Name;

    public static bool IsAdministrator(HttpContext httpContext)
        => httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}

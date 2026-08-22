using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class ApiUser
{
    public static string? BuyerId(HttpContext httpContext) =>
        httpContext.User.Identity?.Name
        ?? httpContext.User.FindFirstValue(ClaimTypes.Name);

    public static bool IsAdministrator(HttpContext httpContext) =>
        httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}

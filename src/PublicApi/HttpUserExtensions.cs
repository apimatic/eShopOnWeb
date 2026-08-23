using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string? GetUserName(this HttpContext httpContext)
    {
        return httpContext.User.Identity?.Name
            ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
            ?? httpContext.User.FindFirstValue("unique_name");
    }

    public static bool IsAdministrator(this HttpContext httpContext)
    {
        return httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
    }
}

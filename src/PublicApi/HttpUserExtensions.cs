using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string? GetBuyerId(this HttpContext httpContext)
    {
        return httpContext.User.Identity?.Name
               ?? httpContext.User.FindFirstValue(ClaimTypes.Name);
    }

    public static bool IsAdministrator(this HttpContext httpContext)
    {
        return httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
    }
}

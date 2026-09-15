using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpContextUserExtensions
{
    public static string? GetUserName(this HttpContext httpContext)
        => httpContext.User.Identity?.Name
           ?? httpContext.User.FindFirstValue(ClaimTypes.Name);

    public static string GetRequiredUserName(this HttpContext httpContext)
    {
        var name = httpContext.GetUserName();
        if (string.IsNullOrEmpty(name))
        {
            throw new UnauthorizedAccessException("The caller is not authenticated.");
        }

        return name;
    }

    public static bool IsAdministrator(this HttpContext httpContext)
        => httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}

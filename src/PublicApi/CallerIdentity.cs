using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class CallerIdentity
{
    public static string RequireBuyerId(HttpContext httpContext)
    {
        var name = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            var fallback = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                           ?? httpContext.User.FindFirstValue(ClaimTypes.Name);
            if (string.IsNullOrWhiteSpace(fallback))
            {
                throw new UnauthorizedAccessException("The caller identity is missing from the token.");
            }

            return fallback;
        }

        return name;
    }

    public static bool IsAdministrator(HttpContext httpContext)
        => httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}

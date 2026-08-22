using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string GetBuyerId(this HttpContext httpContext)
    {
        var name = httpContext.User.Identity?.Name
            ?? httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new UnauthorizedAccessException();
        }

        return name;
    }

    public static bool IsAdministrator(this HttpContext httpContext)
        => httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}

using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new UnauthorizedAccessException("The caller is not authenticated.");
        }

        return name;
    }

    public static bool IsAdministrator(this ClaimsPrincipal user)
    {
        return user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
    }

    public static string GetBuyerId(this HttpContext httpContext) => httpContext.User.GetBuyerId();
}

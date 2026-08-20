using System;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpContextUserExtensions
{
    public static string GetRequiredBuyerId(this HttpContext httpContext)
    {
        var name = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new UnauthorizedAccessException("The caller is not authenticated.");
        }

        return name;
    }

    public static bool IsAdministrator(this HttpContext httpContext) =>
        httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}

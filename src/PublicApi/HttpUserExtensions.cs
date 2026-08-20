using System;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string GetRequiredBuyerId(this HttpContext httpContext)
    {
        var buyerId = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new UnauthorizedAccessException("The caller is not authenticated.");
        }

        return buyerId;
    }

    public static bool IsAdministrator(this HttpContext httpContext)
    {
        return httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
    }
}

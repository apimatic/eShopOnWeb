using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class EndpointIdentity
{
    public static string GetRequiredBuyerId(HttpContext httpContext)
    {
        var buyerId = httpContext.User.Identity?.Name
            ?? httpContext.User.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrEmpty(buyerId))
        {
            throw new UnauthorizedAccessException("The caller is not authenticated.");
        }

        return buyerId;
    }

    public static bool IsAdministrator(HttpContext httpContext)
    {
        return httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
    }
}

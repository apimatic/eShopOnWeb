using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpContextBuyerExtensions
{
    public static string GetBuyerId(this HttpContext httpContext)
    {
        var name = httpContext.User.Identity?.Name
                   ?? httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new UnauthorizedAccessException("The caller identity is missing from the token.");
        }

        return name;
    }
}

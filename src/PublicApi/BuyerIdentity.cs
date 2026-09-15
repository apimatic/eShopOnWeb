using System;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class BuyerIdentity
{
    public static string RequireBuyerId(HttpContext httpContext)
    {
        var name = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new UnauthorizedAccessException();
        }

        return name;
    }
}

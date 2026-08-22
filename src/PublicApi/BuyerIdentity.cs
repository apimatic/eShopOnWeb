using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class BuyerIdentity
{
    public static string RequireBuyerId(HttpContext httpContext)
    {
        var buyerId = httpContext.User.Identity?.Name
            ?? httpContext.User.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new UnauthorizedAccessException("The caller is not authenticated.");
        }

        return buyerId;
    }
}

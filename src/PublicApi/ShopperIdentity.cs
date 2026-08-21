using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class ShopperIdentity
{
    public static string GetRequiredBuyerId(HttpContext httpContext)
    {
        var buyerId = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            buyerId = httpContext.User.FindFirstValue(ClaimTypes.Name);
        }

        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new UnauthorizedAccessException("The caller is not authenticated.");
        }

        return buyerId;
    }
}

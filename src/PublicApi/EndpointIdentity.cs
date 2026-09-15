using System;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class EndpointIdentity
{
    public static string? GetBuyerId(HttpContext http) => http.User.Identity?.Name;

    public static string RequireBuyerId(HttpContext http)
    {
        var buyerId = GetBuyerId(http);
        if (string.IsNullOrEmpty(buyerId))
        {
            throw new UnauthorizedAccessException();
        }

        return buyerId;
    }
}

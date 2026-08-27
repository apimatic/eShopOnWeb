using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

public static class HttpContextExtensions
{
    /// <summary>
    /// The caller's identity, taken from the JWT.
    /// </summary>
    public static string GetBuyerId(this HttpContext httpContext)
    {
        var buyerId = httpContext.User.FindFirst(ClaimTypes.Name)?.Value
            ?? httpContext.User.FindFirst("name")?.Value
            ?? httpContext.User.FindFirst("unique_name")?.Value
            ?? httpContext.User.Identity?.Name;

        if (string.IsNullOrEmpty(buyerId))
        {
            throw new InvalidOperationException("The bearer token does not contain a name claim.");
        }
        return buyerId;
    }
}

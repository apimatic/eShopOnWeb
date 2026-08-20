using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpCaller
{
    public static string? BuyerId(HttpContext httpContext)
        => httpContext.User.Identity?.Name
           ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
           ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

    public static IResult? UnauthorizedIfAnonymous(HttpContext httpContext)
        => string.IsNullOrEmpty(BuyerId(httpContext)) ? Results.Unauthorized() : null;
}

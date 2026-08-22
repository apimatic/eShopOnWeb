using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class EndpointIdentity
{
    public static string? BuyerId(this HttpContext httpContext)
        => httpContext.User.Identity?.Name;

    public static string? BuyerId(this ClaimsPrincipal user)
        => user.Identity?.Name;
}

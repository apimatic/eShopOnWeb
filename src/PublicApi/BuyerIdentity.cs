using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class BuyerIdentity
{
    public static string? GetBuyerId(HttpContext httpContext) =>
        httpContext.User.Identity?.Name
        ?? httpContext.User.FindFirstValue(ClaimTypes.Name);
}

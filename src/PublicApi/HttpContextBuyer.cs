using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpContextBuyer
{
    public static string? GetBuyerId(HttpContext httpContext)
    {
        return httpContext.User.Identity?.Name
            ?? httpContext.User.FindFirstValue(ClaimTypes.Name);
    }
}

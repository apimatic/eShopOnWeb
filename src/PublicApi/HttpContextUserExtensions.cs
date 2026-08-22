using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpContextUserExtensions
{
    public static string? GetBuyerId(this HttpContext httpContext)
    {
        return httpContext.User.Identity?.Name
               ?? httpContext.User.FindFirstValue(ClaimTypes.Name);
    }
}

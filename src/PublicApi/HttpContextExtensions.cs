using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpContextExtensions
{
    public static string? GetBuyerId(this HttpContext httpContext)
        => httpContext.User.Identity?.Name
           ?? httpContext.User.FindFirstValue(ClaimTypes.Name);
}

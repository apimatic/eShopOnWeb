using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class CallerIdentity
{
    public static string? GetBuyerId(HttpContext httpContext) => httpContext.User.Identity?.Name;
}

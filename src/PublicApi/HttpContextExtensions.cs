using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpContextExtensions
{
    public static string RequireBuyerId(this HttpContext httpContext)
    {
        var name = httpContext.User.Identity?.Name
                   ?? httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentException("A signed-in shopper is required.", 401);
        }

        return name;
    }
}

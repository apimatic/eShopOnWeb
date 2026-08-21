using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string RequireBuyerId(this HttpContext httpContext)
    {
        var name = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new OrderPaymentException("A signed-in shopper is required.", 401, "UNAUTHENTICATED");
        }

        return name;
    }
}

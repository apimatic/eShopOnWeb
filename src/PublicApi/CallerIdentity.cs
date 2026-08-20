using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class CallerIdentity
{
    public static string GetBuyerId(HttpContext? httpContext)
    {
        var name = httpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentForbiddenException("The caller is not authenticated.");
        }

        return name;
    }
}

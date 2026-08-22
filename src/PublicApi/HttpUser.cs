using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUser
{
    public static string RequireBuyerId(HttpContext http)
    {
        var name = http.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CheckoutException("The caller is not authenticated.", 401);
        }

        return name;
    }
}

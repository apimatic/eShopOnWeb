using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string RequireBuyerId(this HttpContext http)
    {
        var name = http.User.Identity?.Name ?? http.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(name))
        {
            throw new CheckoutException(401, "Authentication required.");
        }

        return name;
    }
}

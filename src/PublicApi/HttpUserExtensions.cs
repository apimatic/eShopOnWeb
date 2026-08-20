using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string RequireBuyerId(this HttpContext http)
    {
        var name = http.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = http.User.FindFirstValue(ClaimTypes.Name);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentException("A signed-in shopper is required.", 401);
        }

        return name;
    }
}

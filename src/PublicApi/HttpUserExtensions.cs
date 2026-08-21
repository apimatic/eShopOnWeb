using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string RequireBuyerId(this HttpContext httpContext)
    {
        var name = httpContext.User?.Identity?.Name
                   ?? httpContext.User?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentException(401, "Authentication is required.");
        }

        return name;
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class HttpUserExtensions
{
    public static string RequireUserName(this HttpContext httpContext)
    {
        var name = httpContext.User.Identity?.Name ?? httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ApplicationCore.Exceptions.PaymentException(401, "The caller is not authenticated.");
        }

        return name;
    }
}

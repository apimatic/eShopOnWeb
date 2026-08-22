using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string RequireBuyerId(this HttpContext http)
    {
        var buyerId = http.User.Identity?.Name ?? http.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("The caller is not authenticated.", 401);
        }

        return buyerId;
    }
}

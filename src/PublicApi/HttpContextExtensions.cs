using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpContextExtensions
{
    public static string GetRequiredUserName(this HttpContext context)
    {
        var name = context.User.Identity?.Name
                   ?? context.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentException(401, "A signed-in shopper is required.");
        }

        return name;
    }
}

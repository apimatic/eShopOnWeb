using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

public static class EndpointHelpers
{
    public static string GetBuyerId(HttpContext context)
    {
        var name = context.User?.FindFirst(ClaimTypes.Name)?.Value
            ?? context.User?.Identity?.Name;

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentOperationException(401, "The access token does not identify a user.");
        }

        return name!;
    }
}

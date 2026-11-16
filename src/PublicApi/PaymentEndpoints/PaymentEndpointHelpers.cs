using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public static class PaymentEndpointHelpers
{
    public static string GetBuyerId(HttpContext? context)
    {
        var user = context?.User;
        var buyerId = user?.Identity?.Name
                      ?? user?.FindFirst(ClaimTypes.Name)?.Value
                      ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? user?.FindFirst("name")?.Value
                      ?? user?.FindFirst("unique_name")?.Value
                      ?? user?.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(HttpStatusCode.Unauthorized,
                "The access token does not identify a shopper.");
        }

        return buyerId;
    }
}

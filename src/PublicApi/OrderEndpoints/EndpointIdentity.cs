using System.Security.Claims;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class EndpointIdentity
{
    public static string GetBuyerId(ClaimsPrincipal user)
    {
        var buyerId = user.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(
                401,
                "AUTHENTICATION_REQUIRED",
                "The bearer token does not identify a shopper.");
        }

        return buyerId;
    }
}

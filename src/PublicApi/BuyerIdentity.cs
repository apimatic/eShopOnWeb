using System.Net;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class BuyerIdentity
{
    public static string GetRequiredBuyerId(ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("The caller is not authenticated.", HttpStatusCode.Unauthorized);
        }

        return buyerId;
    }
}

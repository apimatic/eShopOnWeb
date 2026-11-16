using System.Security.Claims;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class ClaimsHelper
{
    public static string GetBuyerId(ClaimsPrincipal user)
    {
        var name = user?.FindFirst(ClaimTypes.Name)?.Value
                   ?? user?.Identity?.Name;

        if (string.IsNullOrWhiteSpace(name))
            throw new FinixPaymentException(401, "BUYER_IDENTITY_REQUIRED",
                "The bearer token does not carry a shopper identity.");

        return name;
    }
}

using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException(401, "The caller is not authenticated.", "UNAUTHENTICATED");
        }

        return buyerId;
    }
}

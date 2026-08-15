using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Resolves the caller's shopper identity from the JWT, so every shopper-scoped endpoint acts only on the token's owner.</summary>
internal static class CallerContext
{
    public static string BuyerId(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(name))
        {
            throw new PaymentApiException(401, "The token does not identify a user.");
        }
        return name;
    }
}

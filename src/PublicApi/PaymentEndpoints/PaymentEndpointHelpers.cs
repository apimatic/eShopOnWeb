using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class PaymentEndpointHelpers
{
    /// <summary>The caller's identity taken from the JWT (the Name claim), used as the
    /// shopper's buyer id. Never trusts a client-supplied value.</summary>
    public static string GetCallerId(ClaimsPrincipal user)
    {
        var id = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new PaymentException("The authenticated caller has no identity claim.");
        }
        return id;
    }
}

using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>Resolves the caller's identity (used as the buyer id) from the JWT.</summary>
public static class CallerContext
{
    /// <summary>
    /// The stable identifier for the signed-in shopper, taken from the token. All shopper-scoped data
    /// is keyed on this, so one shopper can never see or act on another's.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.Name)
            ?? user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidPaymentRequestException("The caller's identity could not be determined from the token.");
        }

        return id;
    }
}

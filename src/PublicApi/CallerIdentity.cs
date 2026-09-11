using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Resolves the calling shopper's identity from the validated JWT.</summary>
public static class CallerIdentity
{
    /// <summary>
    /// The buyer id used throughout the app is the user name (email) carried in the token's name claim —
    /// the same value the existing order/basket model keys on.
    /// </summary>
    public static string GetBuyerId(ClaimsPrincipal user)
    {
        var name = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrEmpty(name))
        {
            throw new PaymentValidationException("The access token does not identify a user.");
        }
        return name;
    }
}

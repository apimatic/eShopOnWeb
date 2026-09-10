using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The buyer identity for the calling shopper, taken straight from the JWT (the name claim,
    /// which is the username orders and saved cards are keyed on). Never trust a client-supplied id.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrEmpty(name))
            throw new PaymentException("The authenticated caller has no identity claim.");
        return name;
    }
}

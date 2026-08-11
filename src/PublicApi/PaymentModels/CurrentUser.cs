using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

public static class CurrentUser
{
    /// <summary>
    /// The caller's identity, taken from the JWT (the name claim = username). This is the buyer id
    /// used to scope every shopper operation to the caller's own data.
    /// </summary>
    public static string BuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
            throw new PaymentValidationException("The access token does not identify a user.");
        return name;
    }
}

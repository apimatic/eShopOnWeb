using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Resolves the caller's identity (the shopper id used to scope orders, payments and saved cards)
/// from the JWT. The identity is the authenticated user name carried in the token — the same value
/// the storefront uses as an order's buyer id.
/// </summary>
public static class CallerIdentity
{
    public static string BuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(name))
        {
            throw new PaymentException("The caller's identity could not be determined from the token.");
        }
        return name;
    }
}

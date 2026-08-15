using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The buyer identity for the caller — the user name carried in the token (ClaimTypes.Name),
    /// which is the same value used as an order's BuyerId throughout eShopOnWeb.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(name))
        {
            // Should never happen behind [Authorize]; guard anyway rather than act on a null identity.
            throw new OrderRequestInvalidException("The authenticated caller has no user identity.");
        }
        return name;
    }
}

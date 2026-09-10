using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class PaymentEndpointExtensions
{
    /// <summary>
    /// The caller's identity (username) taken from the JWT, used as the BuyerId across
    /// orders, payments and saved cards. Endpoints are [Authorize]d, so this is present.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(name))
        {
            throw new ResourceForbiddenException("The request is not associated with a signed-in user.");
        }
        return name;
    }
}

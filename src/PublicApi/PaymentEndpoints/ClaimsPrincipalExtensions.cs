using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public static class ClaimsPrincipalExtensions
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = user.FindFirstValue(ClaimTypes.Name);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentException("The bearer token does not contain a user identity.", 401,
                "IDENTITY_MISSING");
        }

        return name;
    }
}

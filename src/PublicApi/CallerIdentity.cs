using System.Security.Claims;
using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class CallerIdentity
{
    public static string BuyerId(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new PaymentException("The caller is not authenticated.", HttpStatusCode.Unauthorized);
        return name;
    }
}

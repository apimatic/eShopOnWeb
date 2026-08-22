using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

public static class HttpUserExtensions
{
    public static string GetRequiredUserName(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new OrderPaymentException(401, "The caller is not authenticated.");
        }

        return name;
    }
}

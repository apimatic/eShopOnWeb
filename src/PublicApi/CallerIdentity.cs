using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class CallerIdentity
{
    public static string GetBuyerId(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ForbiddenAccessException("The caller is not authenticated.");
        }

        return name;
    }
}

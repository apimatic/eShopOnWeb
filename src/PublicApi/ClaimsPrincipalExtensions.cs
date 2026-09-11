using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's identity (username), taken from the JWT name claim. Orders and saved cards
    /// are scoped to this value.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user?.Identity?.Name ?? user?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(name))
            throw new ForbiddenActionException("The caller identity could not be determined from the token.");
        return name;
    }
}

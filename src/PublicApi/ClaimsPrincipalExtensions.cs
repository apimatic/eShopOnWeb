using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's identity from the JWT (the token carries the username as ClaimTypes.Name).
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;
        if (string.IsNullOrEmpty(name))
            throw new PaymentConflictException("The caller's token does not carry an identity claim.");
        return name;
    }
}

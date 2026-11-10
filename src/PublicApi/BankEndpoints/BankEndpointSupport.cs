using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

internal static class BankEndpointSupport
{
    /// <summary>The caller's identity always comes from the JWT.</summary>
    public static string Caller(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name ?? user.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrWhiteSpace(name)) throw new BankingException(401, "The token does not identify a user.");
        return name;
    }
}

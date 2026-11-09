using System.Security.Claims;
using Microsoft.eShopWeb.PublicApi.Stax;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The caller's identity: the shop signs tokens with the shopper's email as the name claim.</summary>
    public static string Email(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name)) throw new ApiException(401, "The token does not identify a shopper.");
        return name.Trim();
    }
}

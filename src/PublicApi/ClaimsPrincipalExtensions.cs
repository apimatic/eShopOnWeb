using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The identity that owns a shopper's data. The JWT carries the username in the Name
    /// claim (see IdentityTokenClaimService); orders and saved cards are scoped to it.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Name)
           ?? user.Identity?.Name
           ?? string.Empty;
}

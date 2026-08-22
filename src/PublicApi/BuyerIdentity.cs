using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

internal static class BuyerIdentity
{
    public static string? GetBuyerId(ClaimsPrincipal user)
        => user.Identity?.Name;
}

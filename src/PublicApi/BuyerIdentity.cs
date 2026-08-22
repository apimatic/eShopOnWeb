using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class BuyerIdentity
{
    public static string? GetBuyerId(this ClaimsPrincipal user)
        => user.Identity?.Name;
}

using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

internal static class BuyerIdentity
{
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true ? user.Identity.Name : null;
}

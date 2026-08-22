using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.AuthEndpoints;

public static class ClaimsPrincipalExtensions
{
    public static string? GetBuyerId(this ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true ? user.Identity.Name : null;
}

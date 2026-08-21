using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

internal static class ClaimsPrincipalExtensions
{
    public static string? GetBuyerId(this ClaimsPrincipal user)
        => user.Identity?.Name;
}

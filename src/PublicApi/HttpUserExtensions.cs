using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string? GetBuyerId(this ClaimsPrincipal user) => user.Identity?.Name;
}

using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Services;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The caller's identity from the JWT token (ClaimTypes.Name), never from request data.</summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) => user.Identity?.Name;
}

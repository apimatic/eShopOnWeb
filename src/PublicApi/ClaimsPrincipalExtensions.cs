using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The caller's username (email), taken from the JWT's name claim.</summary>
    public static string? GetUserName(this ClaimsPrincipal? principal) =>
        principal?.FindFirstValue(ClaimTypes.Name);
}

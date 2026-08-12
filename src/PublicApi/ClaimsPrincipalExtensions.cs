using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's identity (their user name / email), taken from the token. This is the buyer id
    /// used to scope every shopper-owned resource.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Name) ?? principal.Identity?.Name;
}

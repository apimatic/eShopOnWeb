using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Payments;

internal static class ClaimsPrincipalExtensions
{
    /// <summary>The caller's identity (username) taken from the JWT — the buyer id used to scope their data.</summary>
    public static string GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
}

using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The buyer identity for the signed-in caller — taken from the JWT name claim, the same value the
    /// app issues as the token subject. Shopper-scoped endpoints act only on data owned by this id.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user) =>
        user.Identity?.Name
        ?? user.FindFirstValue(ClaimTypes.Name)
        ?? string.Empty;
}

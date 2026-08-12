using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The identity of the calling shopper, taken from the token. Used as the owner key for the
    /// caller's contact numbers, orders and notifications so one shopper can never see or act on
    /// another's data.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name ?? string.Empty;
}

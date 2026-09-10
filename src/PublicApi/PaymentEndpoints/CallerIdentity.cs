using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class CallerIdentity
{
    /// <summary>
    /// The signed-in shopper's identity — the JWT's name claim (username/email), which this app's token
    /// carries. Used as the owner key for orders and saved cards.
    /// </summary>
    public static string Require(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(name)
            ? throw new System.UnauthorizedAccessException("The token does not identify a user.")
            : name;
    }
}

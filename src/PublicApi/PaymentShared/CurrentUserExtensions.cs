using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentShared;

/// <summary>
/// Reads the caller's identity from the validated JWT. The buyer id is the token's name claim
/// (the user name / email), matching how the app already keys orders by <c>BuyerId</c>.
/// </summary>
public static class CurrentUserExtensions
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(buyerId))
        {
            // Should never happen behind [Authorize]; guards against a mis-issued token.
            throw new System.UnauthorizedAccessException("The access token does not identify a user.");
        }

        return buyerId;
    }
}

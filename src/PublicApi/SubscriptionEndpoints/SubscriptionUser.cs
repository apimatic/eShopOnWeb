using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Derives the Maxio customer identity for the caller from their authenticated JWT.
/// The token carries the user's name (email); it is used as the stable Maxio customer reference.
/// </summary>
internal static class SubscriptionUser
{
    public static bool TryResolve(ClaimsPrincipal principal, out MaxioCustomerIdentity identity)
    {
        identity = null!;

        var userName = principal.Identity?.Name
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrWhiteSpace(userName))
        {
            return false;
        }

        var email = principal.FindFirstValue(ClaimTypes.Email) ?? userName;
        var localPart = userName.Contains('@') ? userName[..userName.IndexOf('@')] : userName;
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;

        // reference == userName keeps the same eShop user mapped to a single Maxio customer across calls.
        identity = new MaxioCustomerIdentity(
            reference: userName,
            email: email,
            firstName: firstName,
            lastName: "eShopOnWeb");

        return true;
    }
}

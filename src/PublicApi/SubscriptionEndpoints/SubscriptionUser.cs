using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the stable customer reference from the authenticated principal. The reference is the user's
/// username/email — the same value used to make provider-side customer creation idempotent.
/// </summary>
internal static class SubscriptionUser
{
    public static string GetReference(ClaimsPrincipal user)
    {
        var reference = user?.Identity?.Name;

        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new InvalidSubscriptionOperationException(
                "The authenticated principal carries no username, so no billing customer can be identified.");
        }

        return reference;
    }
}

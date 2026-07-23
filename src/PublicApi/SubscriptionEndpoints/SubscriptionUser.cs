using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the stable eShopOnWeb user reference from the bearer token. The reference is the user's
/// username/email — the same value the storefront uses — so both hosts address the same
/// provider-side customer record.
/// </summary>
public static class SubscriptionUser
{
    public static string ReferenceOf(ClaimsPrincipal user)
    {
        var reference = user.Identity?.Name;

        if (string.IsNullOrWhiteSpace(reference))
        {
            // Authorization has already run, so a token without a name claim is a malformed token
            // rather than an anonymous caller.
            throw new InvalidSubscriptionOperationException(
                "The bearer token carries no user name, so no billing customer can be identified.");
        }

        return reference;
    }

    public static bool IsAdministrator(ClaimsPrincipal user)
    {
        return user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
    }
}

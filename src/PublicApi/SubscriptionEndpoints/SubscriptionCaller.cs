using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the calling JWT principal into the two values the subscription service needs: the
/// caller's own reference, and the ownership scope an operation runs under.
/// </summary>
internal static class SubscriptionCaller
{
    /// <summary>The caller's stable eShopOnWeb reference (their username/email).</summary>
    public static string RequireUserReference(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidSubscriptionOperationException("The access token does not carry a user name claim.");
        }

        return name;
    }

    /// <summary>
    /// The ownership scope for an operation on a specific subscription: administrators act on any
    /// subscription (null), every other caller is confined to their own.
    /// </summary>
    public static string? ResolveOwnerScope(ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)
            ? null
            : RequireUserReference(user);
}

using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves which eShopOnWeb user a subscription request applies to.
/// </summary>
internal static class SubscriptionCaller
{
    /// <summary>
    /// Returns the caller's own user reference, unless an administrator explicitly targeted
    /// another user. Returns <c>null</c> when a non-administrator tries to act on someone else,
    /// so the endpoint can refuse rather than silently acting on the caller's own subscription.
    /// </summary>
    public static string? ResolveUserReference(ClaimsPrincipal caller, string? requestedUserReference)
    {
        var callerReference = caller.Identity?.Name;

        if (string.IsNullOrWhiteSpace(requestedUserReference)
            || string.Equals(requestedUserReference, callerReference, System.StringComparison.OrdinalIgnoreCase))
        {
            return callerReference;
        }

        return caller.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)
            ? requestedUserReference
            : null;
    }
}

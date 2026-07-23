using System;
using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Works out, from the bearer token, which subscriptions a caller may act on.
/// </summary>
/// <remarks>
/// An ordinary caller is confined to their own subscriptions; an administrator may act on any
/// (plan.md §2.4, §4.1). The resulting value is passed straight to <c>ISubscriptionService</c> as its
/// <c>restrictToUserReference</c>, so authorization is enforced in the domain rather than in each endpoint.
/// </remarks>
public static class SubscriptionCaller
{
    /// <summary>The signed-in user's reference, or null when the token carries no name claim.</summary>
    public static string? UserReference(ClaimsPrincipal? user)
    {
        var name = user?.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    /// The restriction to apply: <see langword="null"/> for an administrator (no restriction), otherwise the
    /// caller's own reference.
    /// </summary>
    public static string? Restriction(ClaimsPrincipal? user)
    {
        if (user is not null && user.IsInRole(Constants.Roles.ADMINISTRATORS))
        {
            return null;
        }

        return UserReference(user)
            ?? throw new InvalidOperationException("The bearer token carries no user name claim.");
    }
}

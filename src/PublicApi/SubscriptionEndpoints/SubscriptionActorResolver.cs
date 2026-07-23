using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the authenticated JWT principal into the typed actor the subscription service expects.
/// </summary>
/// <remarks>
/// Administrators may act on any customer's subscription; everyone else is confined to their own,
/// and the service enforces that. Resolving the actor in one place keeps that distinction from
/// being re-derived — and possibly mis-derived — in each endpoint.
/// </remarks>
public static class SubscriptionActorResolver
{
    /// <summary>
    /// Resolves the actor, or <see langword="null"/> when the principal carries no usable identity.
    /// </summary>
    public static SubscriptionActor? Resolve(ClaimsPrincipal? principal)
    {
        var userName = principal?.Identity?.Name;

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return principal!.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)
            ? SubscriptionActor.Administrator()
            : SubscriptionActor.Customer(userName);
    }

    /// <summary>The signed-in user name, or <see langword="null"/> when there is not one.</summary>
    public static string? ResolveUserName(ClaimsPrincipal? principal)
    {
        var userName = principal?.Identity?.Name;

        return string.IsNullOrWhiteSpace(userName) ? null : userName;
    }
}

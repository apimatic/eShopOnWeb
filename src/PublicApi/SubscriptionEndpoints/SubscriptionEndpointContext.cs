using System.Security.Claims;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The <c>TDependency</c> passed to <c>IEndpoint&lt;,,&gt;.HandleAsync</c> for every subscription endpoint
/// that must act as the caller: bundles the service plus the identity resolved from the JWT (mirrors
/// <c>User.Identity.Name</c> / role checks already used in <c>OrderController</c>/<c>CreateCatalogItemEndpoint</c>).
/// </summary>
public sealed record SubscriptionEndpointContext(ISubscriptionService SubscriptionService, string UserId, bool IsAdmin)
{
    public static SubscriptionEndpointContext From(ISubscriptionService subscriptionService, ClaimsPrincipal user)
    {
        var userId = user.Identity?.Name;
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));

        return new SubscriptionEndpointContext(
            subscriptionService,
            userId,
            user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS));
    }
}

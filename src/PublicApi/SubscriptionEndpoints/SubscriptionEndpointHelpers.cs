using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointHelpers
{
    // Administrators may act on any subscription (ownerReference null skips the ownership
    // check in ISubscriptionService); everyone else is scoped to their own subscriptions.
    public static string? ResolveOwnerReference(ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS) ? null : user.Identity!.Name!;

    public static string RequireCallerReference(ClaimsPrincipal user) => user.Identity!.Name!;

    public static CustomerSubscriptionDto ToDto(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        CustomerReference = subscription.CustomerReference,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        NextPlanHandle = subscription.NextPlanHandle,
        BalanceInCents = subscription.BalanceInCents,
    };
}

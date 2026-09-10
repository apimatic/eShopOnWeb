using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps subscription domain models to their API DTOs, and derives the authenticated
/// subscriber identity from the caller's token (never from the request body).
/// </summary>
internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        PriceInCents = plan.PriceInCents,
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        PriceInCents = subscription.PriceInCents,
        Currency = subscription.Currency,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        CurrentPeriodStartsAt = subscription.CurrentPeriodStartsAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        Reference = subscription.Reference
    };

    /// <summary>
    /// Builds the <see cref="SubscriberIdentity"/> from the JWT principal. The username claim
    /// (an email in eShopOnWeb) is the stable reference used to find-or-create the billing
    /// customer. Returns null when the token carries no usable identity.
    /// </summary>
    public static SubscriberIdentity? ToSubscriber(this ClaimsPrincipal? user)
    {
        var name = user?.Identity?.Name
            ?? user?.FindFirstValue(ClaimTypes.Name)
            ?? user?.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var email = user?.FindFirstValue(ClaimTypes.Email) ?? name;
        return new SubscriberIdentity(reference: name, email: email);
    }
}

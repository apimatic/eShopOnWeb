using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapper
{
    public static SubscriptionPlanDto ToDto(MaxioPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Price = plan.PriceInCents / 100m,
        IntervalCount = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static SubscriptionDto ToDto(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.ProductHandle,
        PlanName = subscription.ProductName,
        Price = subscription.PriceInCents / 100m,
        IntervalCount = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        State = subscription.State,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
    };

    /// <summary>
    /// Subscription states Maxio considers not-cancelled/not-expired - used to decide whether an
    /// existing subscription to a plan should be reused instead of creating a duplicate.
    /// </summary>
    public static bool IsLive(string state) =>
        state is "active" or "trialing" or "assessing" or "soft_failure" or "past_due" or "unpaid";
}

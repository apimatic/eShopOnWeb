using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the billing model onto the API contract.
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
        IntervalLength = plan.IntervalLength,
        IntervalUnit = plan.IntervalUnit,
        PaymentMethodRequired = plan.PaymentMethodRequired,
        HasTrial = plan.HasTrial,
        TrialIntervalLength = plan.TrialIntervalLength,
        TrialIntervalUnit = plan.TrialIntervalUnit,
        PricePointHandle = plan.PricePointHandle,
        PricePointName = plan.PricePointName
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PricePointHandle = subscription.PricePointHandle,
        Price = subscription.Price,
        PriceInCents = subscription.PriceInCents,
        IntervalLength = subscription.IntervalLength,
        IntervalUnit = subscription.IntervalUnit,
        State = subscription.State,
        IsLive = subscription.IsLive,
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        BalanceInCents = subscription.BalanceInCents,
        CustomerId = subscription.CustomerId
    };
}

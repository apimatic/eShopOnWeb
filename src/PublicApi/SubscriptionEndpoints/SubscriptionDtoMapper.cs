using Microsoft.eShopWeb.Maxio.Contracts;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionDtoMapper
{
    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan)
    {
        return new SubscriptionPlanDto
        {
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            Price = plan.Price,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            Currency = plan.Currency,
            RequiresCreditCard = plan.RequiresCreditCard,
            TrialInterval = plan.TrialInterval,
            TrialIntervalUnit = plan.TrialIntervalUnit,
            InitialCharge = plan.InitialCharge
        };
    }

    public static SubscriptionDto ToDto(SubscriptionRecord subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            Price = subscription.Price,
            Interval = subscription.Interval,
            IntervalUnit = subscription.IntervalUnit,
            Currency = subscription.Currency,
            CustomerId = subscription.CustomerId,
            CustomerReference = subscription.CustomerReference,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            Balance = subscription.Balance
        };
    }
}

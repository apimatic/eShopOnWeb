using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the domain's subscription types onto the API's DTOs.
/// </summary>
public static class SubscriptionMapper
{
    public static SubscriptionPlanDto ToDto(this BillingPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    public static SubscriptionDto ToDto(this Subscription subscription) => new()
    {
        Id = subscription.Id,
        CustomerReference = subscription.CustomerReference,
        CustomerId = subscription.CustomerId,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PlanPriceInCents = subscription.PlanPriceInCents,
        PlanPrice = subscription.PlanPrice,
        State = subscription.State.ToString(),
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        DelayedCancelAt = subscription.DelayedCancelAt,
        AutomaticallyResumeAt = subscription.AutomaticallyResumeAt
    };

    public static PlanChangePreviewDto ToDto(this PlanChangePreview preview) => new()
    {
        TargetPlanHandle = preview.TargetPlanHandle,
        Timing = preview.Timing.ToString(),
        ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents,
        ChargeInCents = preview.ChargeInCents,
        PaymentDueInCents = preview.PaymentDueInCents,
        CreditAppliedInCents = preview.CreditAppliedInCents,
        PaymentDue = preview.PaymentDue
    };
}

using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapper
{
    public static SubscriptionDto ToDto(this SubscriptionDetails subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State.ToString(),
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        PriceInCents = subscription.PriceInCents,
        IntervalUnit = subscription.IntervalUnit,
        Interval = subscription.Interval,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        DelayedCancelAt = subscription.DelayedCancelAt,
        OnHoldAt = subscription.OnHoldAt,
        AutomaticallyResumeAt = subscription.AutomaticallyResumeAt
    };

    public static PlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        PriceInCents = plan.PriceInCents,
        IntervalUnit = plan.IntervalUnit,
        Interval = plan.Interval
    };

    public static PlanChangePreviewDto ToDto(this PlanChangePreview preview) => new()
    {
        CurrentProductHandle = preview.CurrentProductHandle,
        TargetProductHandle = preview.TargetProductHandle,
        ApplyImmediately = preview.ApplyImmediately,
        ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents,
        ChargeInCents = preview.ChargeInCents,
        PaymentDueInCents = preview.PaymentDueInCents,
        CreditAppliedInCents = preview.CreditAppliedInCents
    };

    public static PlanChangePreview ToDomain(this PlanChangePreviewDto dto) => new(
        dto.CurrentProductHandle,
        dto.TargetProductHandle,
        dto.ApplyImmediately,
        dto.ProratedAdjustmentInCents,
        dto.ChargeInCents,
        dto.PaymentDueInCents,
        dto.CreditAppliedInCents);
}

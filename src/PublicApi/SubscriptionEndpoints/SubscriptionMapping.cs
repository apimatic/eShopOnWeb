using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapping
{
    public static SubscriptionDto ToDto(Subscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.ProductHandle,
        ProductId = subscription.ProductId,
        ProductPriceInCents = subscription.ProductPriceInCents,
        State = subscription.State.ToString(),
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt
    };

    public static BillingPlanDto ToDto(BillingPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static UsageReportDto ToDto(UsageReport report) => new()
    {
        SubscriptionId = report.SubscriptionId,
        RecordedQuantity = report.RecordedQuantity,
        PeriodToDateTotal = report.PeriodToDateTotal,
        TotalAvailable = report.TotalAvailable
    };

    public static PlanChangePreviewDto ToDto(PlanChangePreview preview) => new()
    {
        SubscriptionId = preview.SubscriptionId,
        CurrentProductHandle = preview.CurrentProductHandle,
        TargetProductHandle = preview.TargetProductHandle,
        Timing = preview.Timing.ToString(),
        ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents,
        ChargeInCents = preview.ChargeInCents,
        PaymentDueInCents = preview.PaymentDueInCents,
        CreditAppliedInCents = preview.CreditAppliedInCents,
        NewPlanPriceInCents = preview.NewPlanPriceInCents,
        EffectiveAt = preview.EffectiveAt
    };

    public static PlanChangePreview FromDto(PlanChangePreviewDto dto) => new(
        dto.SubscriptionId,
        dto.CurrentProductHandle,
        dto.TargetProductHandle,
        Enum.Parse<PlanChangeTiming>(dto.Timing),
        dto.ProratedAdjustmentInCents,
        dto.ChargeInCents,
        dto.PaymentDueInCents,
        dto.CreditAppliedInCents,
        dto.NewPlanPriceInCents,
        dto.EffectiveAt);
}

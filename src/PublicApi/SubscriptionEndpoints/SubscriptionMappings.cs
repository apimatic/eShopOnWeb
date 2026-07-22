using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the subscription domain types onto the API contract. Hand-written rather than mapped by
/// convention because the wire shape is a published contract that should not drift when a domain
/// property is renamed.
/// </summary>
internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit.ToString(),
        BillingPeriod = plan.BillingPeriod,
        Description = plan.Description
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State.ToString(),
        CustomerReference = subscription.CustomerReference,
        CustomerId = subscription.CustomerId,
        PlanId = subscription.PlanId,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PlanPrice = subscription.PlanPrice,
        Currency = subscription.Currency,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        CanceledAt = subscription.CanceledAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        ScheduledCancellationAt = subscription.ScheduledCancellationAt,
        OnHoldAt = subscription.OnHoldAt,
        AutomaticallyResumeAt = subscription.AutomaticallyResumeAt,
        PendingPlanHandle = subscription.PendingPlanHandle,
        IsLive = subscription.IsLive,
        AllowedActions = subscription.AllowedActions.Select(action => action.ToString()).ToList()
    };

    public static UsageSummaryDto ToDto(this UsageSummary summary) => new()
    {
        UsageId = summary.Recorded.Id,
        SubscriptionId = summary.Recorded.SubscriptionId,
        Quantity = summary.Recorded.Quantity,
        Memo = summary.Recorded.Memo,
        RecordedAt = summary.Recorded.RecordedAt,
        IsPeriodTotalAvailable = summary.IsPeriodTotalAvailable,
        PeriodToDateQuantity = summary.PeriodToDateQuantity,
        UnitPrice = summary.UnitPrice,
        PeriodToDateAmount = summary.PeriodToDateAmount,
        PeriodStartedAt = summary.PeriodStartedAt,
        PeriodEndsAt = summary.PeriodEndsAt
    };

    public static PlanChangePreviewDto ToDto(this PlanChangePreview preview) => new()
    {
        SubscriptionId = preview.SubscriptionId,
        CurrentPlanHandle = preview.CurrentPlanHandle,
        CurrentPlanName = preview.CurrentPlanName,
        TargetPlanHandle = preview.TargetPlanHandle,
        TargetPlanName = preview.TargetPlanName,
        Timing = preview.Timing.ToString(),
        ProratedAdjustment = preview.ProratedAdjustment,
        Charge = preview.Charge,
        PaymentDue = preview.PaymentDue,
        CreditApplied = preview.CreditApplied,
        TargetPlanPrice = preview.TargetPlanPrice,
        EffectiveAt = preview.EffectiveAt,
        Signature = preview.Signature
    };
}

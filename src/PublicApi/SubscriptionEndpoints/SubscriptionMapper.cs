using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the subscription domain types onto the API's own contract, so the wire shape stays
/// stable no matter what the billing provider returns.
/// </summary>
internal static class SubscriptionMapper
{
    internal static SubscriptionPlanDto ToDto(this BillingPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    internal static SubscriptionDto ToDto(this Subscription subscription) => new()
    {
        Id = subscription.ProviderSubscriptionId,
        CustomerId = subscription.ProviderCustomerId,
        CustomerReference = subscription.CustomerReference,
        PlanHandle = subscription.Plan.Handle,
        PlanName = subscription.Plan.Name,
        Price = subscription.Plan.Price,
        State = subscription.State.ToString(),
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        ScheduledPlanHandle = subscription.ScheduledPlanHandle
    };

    internal static UsageReportDto ToDto(this UsageReport report) => new()
    {
        UsageId = report.Record.Id,
        SubscriptionId = report.Record.SubscriptionId,
        ComponentHandle = report.Record.ComponentHandle,
        Quantity = report.Record.Quantity,
        Memo = report.Record.Memo,
        RecordedAt = report.Record.RecordedAt,
        PeriodToDateUnits = report.PeriodToDateUnits,
        PeriodToDateUnitsAvailable = report.PeriodToDateUnitsAvailable,
        UnitPrice = report.UnitPrice,
        PeriodToDateCharge = report.PeriodToDateCharge
    };

    internal static PlanChangePreviewDto ToDto(this PlanChangePreview preview) => new()
    {
        CurrentPlanHandle = preview.CurrentPlanHandle,
        TargetPlanHandle = preview.TargetPlanHandle,
        Timing = preview.Timing.ToString(),
        ProratedAdjustment = preview.ProratedAdjustment,
        Charge = preview.Charge,
        PaymentDue = preview.PaymentDue,
        CreditApplied = preview.CreditApplied,
        Fingerprint = preview.Fingerprint
    };
}

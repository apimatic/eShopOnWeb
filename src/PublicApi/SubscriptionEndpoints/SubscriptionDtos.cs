using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable recurring plan. Prices are decimal currency units.
/// </summary>
public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequiresPaymentMethod { get; set; }

    public static SubscriptionPlanDto From(BillingPlan plan) => new()
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
}

/// <summary>
/// A subscription as held by the billing provider.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal? PlanPrice { get; set; }
    public string? CustomerReference { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? DelayedCancelAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
    public string? NextPlanHandle { get; set; }

    public static SubscriptionDto From(BillingSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State.ToString(),
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PlanPrice = subscription.PlanPrice,
        CustomerReference = subscription.CustomerReference,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        DelayedCancelAt = subscription.DelayedCancelAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        NextPlanHandle = subscription.NextPlanHandle
    };
}

/// <summary>
/// The outcome of reporting metered usage.
/// </summary>
public class UsageRecordDto
{
    public long UsageId { get; set; }
    public int SubscriptionId { get; set; }
    public string ComponentHandle { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }

    /// <summary>Running unit balance for the period, or null when the read-back was unavailable.</summary>
    public int? PeriodToDateUnits { get; set; }

    public decimal? PeriodToDateCharge { get; set; }

    public static UsageRecordDto From(UsageRecordResult result) => new()
    {
        UsageId = result.UsageId,
        SubscriptionId = result.SubscriptionId,
        ComponentHandle = result.ComponentHandle,
        Quantity = result.Quantity,
        Memo = result.Memo,
        PeriodToDateUnits = result.PeriodToDateUnits,
        PeriodToDateCharge = result.PeriodToDateCharge
    };
}

/// <summary>
/// A quoted plan change. <see cref="Fingerprint"/> must be echoed back to commit it.
/// </summary>
public class PlanChangePreviewDto
{
    public int SubscriptionId { get; set; }
    public string? CurrentPlanHandle { get; set; }
    public string TargetPlanHandle { get; set; } = string.Empty;
    public string Timing { get; set; } = string.Empty;
    public decimal ProratedAdjustment { get; set; }
    public decimal Charge { get; set; }
    public decimal CreditApplied { get; set; }
    public decimal PaymentDue { get; set; }
    public decimal TargetPlanPrice { get; set; }
    public DateTimeOffset? EffectiveAt { get; set; }

    /// <summary>Echo this back on commit; the change is refused if the quote has moved since.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    public static PlanChangePreviewDto From(PlanChangePreview preview) => new()
    {
        SubscriptionId = preview.SubscriptionId,
        CurrentPlanHandle = preview.CurrentPlanHandle,
        TargetPlanHandle = preview.TargetPlanHandle,
        Timing = preview.Timing.ToString(),
        ProratedAdjustment = preview.ProratedAdjustment,
        Charge = preview.Charge,
        CreditApplied = preview.CreditApplied,
        PaymentDue = preview.PaymentDue,
        TargetPlanPrice = preview.TargetPlanPrice,
        EffectiveAt = preview.EffectiveAt,
        Fingerprint = preview.Fingerprint
    };
}

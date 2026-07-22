using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscription as returned by the API. All money is in whole currency units.</summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal PlanPrice { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? DelayedCancelAt { get; set; }
    public string? ScheduledPlanHandle { get; set; }

    public static SubscriptionDto From(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State.ToString(),
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PlanPrice = subscription.PlanPrice,
        NextBillingDate = subscription.NextBillingDate,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        DelayedCancelAt = subscription.DelayedCancelAt,
        ScheduledPlanHandle = subscription.ScheduledPlanHandle
    };
}

/// <summary>A subscription plan as returned by the API.</summary>
public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }

    public static SubscriptionPlanDto From(SubscriptionPlan plan) => new()
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

/// <summary>The outcome of a usage report, including the running period-to-date total.</summary>
public class UsageSummaryDto
{
    public long UsageId { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }
    public string? ComponentHandle { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>Null when the running total could not be read; the usage itself still stands.</summary>
    public int? PeriodToDateUnits { get; set; }

    public decimal? PeriodToDateCharge { get; set; }

    public static UsageSummaryDto From(UsageSummary summary) => new()
    {
        UsageId = summary.Receipt.Id,
        Quantity = summary.Receipt.Quantity,
        Memo = summary.Receipt.Memo,
        RecordedAt = summary.Receipt.RecordedAt,
        ComponentHandle = summary.Receipt.ComponentHandle,
        UnitPrice = summary.UnitPrice,
        PeriodToDateUnits = summary.PeriodToDateUnits,
        PeriodToDateCharge = summary.PeriodToDateCharge
    };
}

/// <summary>The previewed cost of a plan change, with the fingerprint the commit must echo back.</summary>
public class PlanChangePreviewDto
{
    public int SubscriptionId { get; set; }
    public string CurrentPlanHandle { get; set; } = string.Empty;
    public string TargetPlanHandle { get; set; } = string.Empty;
    public string Timing { get; set; } = string.Empty;
    public bool IsProrated { get; set; }
    public decimal ProratedAdjustment { get; set; }
    public decimal Charge { get; set; }
    public decimal PaymentDue { get; set; }
    public decimal CreditApplied { get; set; }
    public decimal TargetPlanPrice { get; set; }
    public DateTimeOffset? EffectiveAt { get; set; }

    /// <summary>
    /// Send this back on the commit call. If the basis has moved in the meantime the commit is
    /// refused rather than charging an amount that was never shown.
    /// </summary>
    public string Fingerprint { get; set; } = string.Empty;

    public static PlanChangePreviewDto From(PlanChangePreview preview) => new()
    {
        SubscriptionId = preview.SubscriptionId,
        CurrentPlanHandle = preview.CurrentPlanHandle,
        TargetPlanHandle = preview.TargetPlanHandle,
        Timing = preview.Timing.ToString(),
        IsProrated = preview.IsProrated,
        ProratedAdjustment = preview.ProratedAdjustment,
        Charge = preview.Charge,
        PaymentDue = preview.PaymentDue,
        CreditApplied = preview.CreditApplied,
        TargetPlanPrice = preview.TargetPlanPrice,
        EffectiveAt = preview.EffectiveAt,
        Fingerprint = preview.Fingerprint
    };
}

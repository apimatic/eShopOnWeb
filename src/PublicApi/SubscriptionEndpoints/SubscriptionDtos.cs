using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscribable plan.</summary>
public class SubscriptionPlanDto
{
    public long Id { get; set; }

    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in minor units (cents), as the billing provider reports it.</summary>
    public int PriceInCents { get; set; }

    /// <summary>Recurring price in major units, derived from <see cref="PriceInCents"/>.</summary>
    public decimal Price { get; set; }

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    public bool RequiresPaymentMethod { get; set; }

    public static SubscriptionPlanDto FromPlan(BillingPlan plan) => new()
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
}

/// <summary>A customer's subscription.</summary>
public class SubscriptionDto
{
    public long Id { get; set; }

    public string State { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public int PlanPriceInCents { get; set; }

    public decimal PlanPrice { get; set; }

    public string Currency { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? NextBillingDate { get; set; }

    public bool CancelAtEndOfPeriod { get; set; }

    public DateTimeOffset? DelayedCancelAt { get; set; }

    /// <summary>Set when a plan change is already scheduled for the next renewal.</summary>
    public string? NextPlanHandle { get; set; }

    public static SubscriptionDto FromSubscription(Subscription subscription) => new()
    {
        Id = subscription.ProviderSubscriptionId,
        State = subscription.State.ToString(),
        IsActive = subscription.IsActive,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.Billing.ProductName,
        PlanPriceInCents = subscription.Billing.ProductPriceInCents,
        PlanPrice = subscription.Billing.ProductPrice,
        Currency = subscription.Billing.Currency,
        CurrentPeriodStartsAt = subscription.Billing.CurrentPeriodStartsAt,
        CurrentPeriodEndsAt = subscription.Billing.CurrentPeriodEndsAt,
        NextBillingDate = subscription.Billing.NextAssessmentAt,
        CancelAtEndOfPeriod = subscription.Billing.CancelAtEndOfPeriod,
        DelayedCancelAt = subscription.Billing.DelayedCancelAt,
        NextPlanHandle = subscription.Billing.NextProductHandle
    };
}

/// <summary>The outcome of recording metered usage.</summary>
public class UsageRecordDto
{
    public long UsageId { get; set; }

    public long SubscriptionId { get; set; }

    public string ComponentHandle { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public string? Memo { get; set; }

    /// <summary>Running total for the period; null when the read-back was unavailable.</summary>
    public int? PeriodToDateUnits { get; set; }

    public decimal? UnitPrice { get; set; }

    public decimal? PeriodToDateEstimatedCharge { get; set; }

    public static UsageRecordDto FromResult(UsageRecordResult result) => new()
    {
        UsageId = result.UsageId,
        SubscriptionId = result.SubscriptionId,
        ComponentHandle = result.ComponentHandle,
        Quantity = result.Quantity,
        Memo = result.Memo,
        PeriodToDateUnits = result.PeriodToDateUnits,
        UnitPrice = result.UnitPrice,
        PeriodToDateEstimatedCharge = result.PeriodToDateEstimatedCharge
    };
}

/// <summary>A previewed plan change, including the fingerprint required to commit it.</summary>
public class PlanChangePreviewDto
{
    public string TargetPlanHandle { get; set; } = string.Empty;

    public string Timing { get; set; } = string.Empty;

    public int ProratedAdjustmentInCents { get; set; }

    public int ChargeInCents { get; set; }

    public int PaymentDueInCents { get; set; }

    public int CreditAppliedInCents { get; set; }

    public decimal PaymentDue { get; set; }

    /// <summary>
    /// Echo this back on commit. The change is rejected if the provider's numbers moved since the
    /// preview was taken, so the amount charged always matches the amount shown.
    /// </summary>
    public string Fingerprint { get; set; } = string.Empty;

    public static PlanChangePreviewDto FromPreview(PlanChangePreview preview) => new()
    {
        TargetPlanHandle = preview.TargetProductHandle,
        Timing = preview.Timing.ToString(),
        ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents,
        ChargeInCents = preview.ChargeInCents,
        PaymentDueInCents = preview.PaymentDueInCents,
        CreditAppliedInCents = preview.CreditAppliedInCents,
        PaymentDue = preview.PaymentDue,
        Fingerprint = preview.Fingerprint
    };
}

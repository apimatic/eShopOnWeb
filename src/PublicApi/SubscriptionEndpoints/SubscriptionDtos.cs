using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Wire-shape DTOs for the subscription endpoints - never expose ApplicationCore types directly.</summary>
public class PlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public int Interval { get; set; }
    public bool RequiresPaymentMethod { get; set; }

    public static PlanDto FromDomain(BillingPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        PriceInCents = plan.PriceInCents,
        IntervalUnit = plan.IntervalUnit,
        Interval = plan.Interval,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
    };
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public long BalanceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public string? NextProductHandle { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }

    public static SubscriptionDto FromDomain(BillingSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State.ToString(),
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        PriceInCents = subscription.PriceInCents,
        BalanceInCents = subscription.BalanceInCents,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextProductHandle = subscription.NextProductHandle,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
    };
}

public class UsageDto
{
    public long Id { get; set; }
    public double Quantity { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int? PeriodToDateBalance { get; set; }

    public static UsageDto FromDomain(BillingUsage usage) => new()
    {
        Id = usage.Id,
        Quantity = usage.Quantity,
        Memo = usage.Memo,
        CreatedAt = usage.CreatedAt,
        PeriodToDateBalance = usage.PeriodToDateBalance,
    };
}

public class UsageBalanceDto
{
    public int UnitBalance { get; set; }

    public static UsageBalanceDto FromDomain(BillingComponentBalance balance) => new() { UnitBalance = balance.UnitBalance };
}

public class PlanChangePreviewDto
{
    public string TargetPlanHandle { get; set; } = string.Empty;
    public long ProratedAdjustmentInCents { get; set; }
    public long ChargeInCents { get; set; }
    public long PaymentDueInCents { get; set; }
    public long CreditAppliedInCents { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }

    public static PlanChangePreviewDto FromDomain(BillingPlanChangePreview preview) => new()
    {
        TargetPlanHandle = preview.TargetPlanHandle,
        ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents,
        ChargeInCents = preview.ChargeInCents,
        PaymentDueInCents = preview.PaymentDueInCents,
        CreditAppliedInCents = preview.CreditAppliedInCents,
        EffectiveAt = preview.EffectiveAt,
    };

    public BillingPlanChangePreview ToDomain() => new(
        TargetPlanHandle,
        ProratedAdjustmentInCents,
        ChargeInCents,
        PaymentDueInCents,
        CreditAppliedInCents,
        EffectiveAt);
}

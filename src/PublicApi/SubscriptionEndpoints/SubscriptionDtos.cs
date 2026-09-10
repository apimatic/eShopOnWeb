using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscription plan a shopper can enroll in.</summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public string PriceDisplay { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }

    public static SubscriptionPlanDto From(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        PriceDisplay = plan.PriceDisplay,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };
}

/// <summary>A shopper's subscription as held by Maxio (the system of record).</summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string PriceDisplay { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string PaymentCollectionMethod { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }

    public static SubscriptionDto From(SubscriptionSummary s) => new()
    {
        Id = s.Id,
        State = s.State,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        PriceInCents = s.PriceInCents,
        PriceDisplay = s.PriceDisplay,
        Interval = s.Interval,
        IntervalUnit = s.IntervalUnit,
        PaymentCollectionMethod = s.PaymentCollectionMethod,
        CurrentPeriodStartedAt = s.CurrentPeriodStartedAt,
        NextBillingAt = s.NextBillingAt
    };
}

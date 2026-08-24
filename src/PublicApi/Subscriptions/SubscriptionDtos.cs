using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>A subscription plan (Maxio product) available for shoppers to subscribe to.</summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price => PriceInCents / 100m;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }
}

/// <summary>A shopper's subscription as confirmed by Maxio.</summary>
public class SubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price => PriceInCents / 100m;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>When the current billing period ends and the next charge is attempted.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

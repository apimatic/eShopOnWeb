using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscription owned by the caller, as reflected in Maxio (the billing system of record).</summary>
public class SubscriptionDto
{
    /// <summary>The Maxio subscription id.</summary>
    public long Id { get; set; }

    /// <summary>The subscription state in Maxio (e.g. "active").</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>The handle of the subscribed plan (e.g. "eshop-pro").</summary>
    public string? PlanHandle { get; set; }

    /// <summary>The display name of the subscribed plan.</summary>
    public string? PlanName { get; set; }

    /// <summary>The recurring price, in integer cents.</summary>
    public long? PriceInCents { get; set; }

    /// <summary>The recurring price as a decimal amount.</summary>
    public decimal Price { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    /// <summary>When the current billing period started.</summary>
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>The next billing date (end of the current billing period).</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    /// <summary>When the subscription became active.</summary>
    public DateTimeOffset? ActivatedAt { get; set; }

    /// <summary>When the subscription was created in Maxio.</summary>
    public DateTimeOffset? CreatedAt { get; set; }
}

using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription held by the authenticated shopper, as recorded by the billing system.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public long Id { get; set; }

    /// <summary>Handle of the plan the subscription bills on.</summary>
    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public string? PricePointHandle { get; set; }

    /// <summary>Recurring amount in major currency units.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring amount in minor currency units (cents).</summary>
    public long PriceInCents { get; set; }

    public int? IntervalLength { get; set; }

    public string? IntervalUnit { get; set; }

    /// <summary>Billing state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while this subscription entitles the shopper to the plan.</summary>
    public bool IsLive { get; set; }

    /// <summary>When the next charge is scheduled. Null once the subscription has ended.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Outstanding balance in minor currency units.</summary>
    public long BalanceInCents { get; set; }

    /// <summary>Identifier of the billing customer this subscription belongs to.</summary>
    public long CustomerId { get; set; }
}

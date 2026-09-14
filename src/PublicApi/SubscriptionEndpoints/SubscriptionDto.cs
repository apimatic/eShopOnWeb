using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as held by the billing system.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public long Id { get; set; }

    /// <summary>The reference eShopOnWeb assigned to this subscription in the billing system.</summary>
    public string? Reference { get; set; }

    /// <summary>Lifecycle state, e.g. active, trialing, past_due, canceled.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsLive { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring price in major units, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string Currency { get; set; } = string.Empty;

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Amount currently owed on the subscription, in minor units.</summary>
    public long BalanceInCents { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the billing system will next bill this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    /// <summary>Identifier of the billing customer the subscription belongs to.</summary>
    public long CustomerId { get; set; }

    public string? CustomerReference { get; set; }
}

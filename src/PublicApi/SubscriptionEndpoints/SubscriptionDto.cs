using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as it currently stands in the billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public long Id { get; set; }

    public long CustomerId { get; set; }

    /// <summary>The key that ties the billing customer back to this eShopOnWeb account.</summary>
    public string? CustomerReference { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Lifecycle state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the product.</summary>
    public bool IsLive { get; set; }

    public long PriceInCents { get; set; }

    public string FormattedPrice { get; set; } = string.Empty;

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    /// <summary>When the next renewal charge is scheduled.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}

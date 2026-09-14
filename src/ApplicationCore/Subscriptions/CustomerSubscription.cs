using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription as it currently stands in the billing system of record.
/// </summary>
public class CustomerSubscription
{
    public long Id { get; init; }

    public long CustomerId { get; init; }

    /// <summary>The key linking this billing customer back to the eShopOnWeb account.</summary>
    public string? CustomerReference { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>Billing-system lifecycle state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the product.</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);

    public long PriceInCents { get; init; }

    public string FormattedPrice => (PriceInCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    public int? Interval { get; init; }

    public string? IntervalUnit { get; init; }

    /// <summary>When the next renewal charge is scheduled. Null for subscriptions that will not renew.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}

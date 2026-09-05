using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A buyer's subscription, read live from Advanced Billing (Maxio is the system of record;
/// nothing about this shape is persisted locally).
/// </summary>
public class CustomerSubscription
{
    public long MaxioSubscriptionId { get; init; }
    public string State { get; init; } = string.Empty;
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public int IntervalCount { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>The next time Maxio will attempt to assess/charge this subscription, if known.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription as recorded in Maxio, confirmed back to the subscriber.
/// </summary>
public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    /// <summary>Handle of the subscribed plan.</summary>
    public string PlanHandle { get; set; } = string.Empty;
    /// <summary>Display name of the subscribed plan.</summary>
    public string PlanName { get; set; } = string.Empty;
    /// <summary>Current recurring price in cents.</summary>
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    /// <summary>Maxio subscription state (e.g. "active").</summary>
    public string State { get; set; } = string.Empty;
    /// <summary>Next billing date (end of the current period).</summary>
    public DateTimeOffset? NextBillingAt { get; set; }
}

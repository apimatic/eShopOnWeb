using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription as seen by the caller. Maxio (Advanced Billing) is the system of
/// record; these values are a projection of the upstream subscription.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Maxio id of the subscription.</summary>
    public long SubscriptionId { get; set; }

    /// <summary>Maxio subscription reference (idempotency key for the subscription).</summary>
    public string? Reference { get; set; }

    /// <summary>Current subscription state (e.g. active, trialing, past_due, canceled).</summary>
    public string? State { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Recurring price of the subscription in the site's currency.</summary>
    public decimal Price { get; set; }

    public string? Currency { get; set; }

    public int Interval { get; set; } = 1;

    public string? IntervalUnit { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>End of the current billing period; the next scheduled billing date.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}

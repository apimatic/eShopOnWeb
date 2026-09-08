using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A Maxio subscription surfaced by the PublicApi.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Maxio subscription id (unstable across sandbox re-seeds; display only).</summary>
    public int? Id { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Recurring charge in <see cref="Currency"/> per billing interval.</summary>
    public decimal? Price { get; set; }

    /// <summary>ISO currency code the subscription is billed in (e.g. "USD").</summary>
    public string? Currency { get; set; }

    /// <summary>Subscription state as reported by Maxio, e.g. "active", "trialing", "canceled".</summary>
    public string? State { get; set; }

    /// <summary>Next billing/assessment date.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>True when the subscription is in an active or trialing state.</summary>
    public bool IsActive { get; set; }
}

using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

/// <summary>A shopper's subscription as confirmed by Maxio.</summary>
public class SubscriptionDto
{
    /// <summary>The Maxio subscription id.</summary>
    public long SubscriptionId { get; set; }

    /// <summary>The Maxio subscription state (e.g. "active").</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>The Maxio product id of the subscribed plan.</summary>
    public long? PlanId { get; set; }

    /// <summary>The subscribed plan handle.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>The subscribed plan name.</summary>
    public string? PlanName { get; set; }

    /// <summary>The recurring amount in integer cents.</summary>
    public long? PriceInCents { get; set; }

    /// <summary>The recurring amount in dollars.</summary>
    public decimal? Price => PriceInCents.HasValue ? PriceInCents.Value / 100m : null;

    /// <summary>End of the current billing period (the next billing date).</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the subscription was created in Maxio.</summary>
    public DateTimeOffset? CreatedAt { get; set; }
}

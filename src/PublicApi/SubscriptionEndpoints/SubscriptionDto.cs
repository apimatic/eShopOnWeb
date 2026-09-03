using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int? Id { get; set; }
    public string PlanHandle { get; set; }
    public string PlanName { get; set; }

    /// <summary>Recurring price in the plan's currency (normalised from Maxio's integer cents).</summary>
    public decimal Price { get; set; }

    /// <summary>Raw price in cents as reported by Maxio.</summary>
    public long? PriceInCents { get; set; }

    /// <summary>Subscription state as reported by Maxio (e.g. "active", "pending").</summary>
    public string State { get; set; }

    /// <summary>Next assessment / billing date reported by Maxio.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}

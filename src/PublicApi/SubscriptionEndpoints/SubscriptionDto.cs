using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A user's subscription as confirmed by the billing system (Maxio).
/// </summary>
public class SubscriptionDto
{
    /// <summary>Maxio subscription id.</summary>
    public int SubscriptionId { get; set; }

    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring price per billing period.</summary>
    public decimal Price { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>Maxio subscription state (e.g. active, canceled).</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Date of the next billing (renewal) — null for canceled subscriptions.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEnd { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>True when this response is an idempotent replay of an existing subscription rather than a new enrollment.</summary>
    public bool AlreadySubscribed { get; set; }
}

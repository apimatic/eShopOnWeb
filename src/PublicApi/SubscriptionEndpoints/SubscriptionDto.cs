using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring subscription as recorded in Maxio Advanced Billing.
/// </summary>
public class SubscriptionDto
{
    /// <summary>The Maxio subscription id.</summary>
    public long Id { get; set; }
    public string? Reference { get; set; }

    /// <summary>The Maxio product handle of the subscribed plan.</summary>
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring price in cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Maxio subscription state, e.g. active, trialing, canceled.</summary>
    public string State { get; set; } = string.Empty;
    public long? CustomerId { get; set; }
    public DateTime? ActivatedAt { get; set; }

    /// <summary>When the next scheduled charge occurs (end of the current period).</summary>
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
}

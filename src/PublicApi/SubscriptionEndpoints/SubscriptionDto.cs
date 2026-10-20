using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription as Maxio reports it.
/// </summary>
public class SubscriptionDto
{
    /// <summary>State used when the outcome of a subscribe is not yet confirmed with Maxio.</summary>
    public const string UnknownState = "unknown";

    /// <summary>Maxio subscription id; null only while <see cref="State"/> is "unknown".</summary>
    public int? SubscriptionId { get; set; }

    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public PriceDto Price { get; set; } = new();
    public BillingIntervalDto? Interval { get; set; }

    /// <summary>Maxio's subscription state wire value (e.g. "active"), or "unknown" while unconfirmed.</summary>
    public string State { get; set; } = UnknownState;

    /// <summary>When Maxio next bills the subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}

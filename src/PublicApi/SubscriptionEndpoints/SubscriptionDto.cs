using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// API representation of a shopper's subscription as it exists in Maxio.
/// </summary>
public class SubscriptionDto
{
    public int SubscriptionId { get; set; }

    public int CustomerId { get; set; }

    /// <summary>Subscription state, e.g. "active".</summary>
    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public long PriceInCents { get; set; }

    public string FormattedPrice { get; set; } = string.Empty;

    /// <summary>When the next regularly-scheduled charge occurs.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}

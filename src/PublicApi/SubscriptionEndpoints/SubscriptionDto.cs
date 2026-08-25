using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long? ProductPriceInCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>Next billing date (mapped from the subscription's current_period_ends_at).</summary>
    public DateTimeOffset? NextBillingDate { get; set; }
}

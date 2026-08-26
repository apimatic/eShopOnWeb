using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Renewal / next billing date (Maxio current_period_ends_at).
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public bool CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
}

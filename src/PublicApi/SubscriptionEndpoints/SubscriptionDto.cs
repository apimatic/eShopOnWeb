using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int? SubscriptionId { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}

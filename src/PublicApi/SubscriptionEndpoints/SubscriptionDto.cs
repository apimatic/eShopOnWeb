using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public long PriceInCents { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
}

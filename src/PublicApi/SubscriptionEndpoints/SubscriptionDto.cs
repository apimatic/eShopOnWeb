using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int? Id { get; set; }
    public string? State { get; set; }
    public string? ProductName { get; set; }
    public string? ProductHandle { get; set; }
    public long? PriceInCents { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public string? Reference { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
}

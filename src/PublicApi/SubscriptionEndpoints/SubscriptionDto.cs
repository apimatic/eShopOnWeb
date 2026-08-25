using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int? Id { get; set; }
    public string? State { get; set; }
    public string? ProductName { get; set; }
    public string? ProductHandle { get; set; }
    public long? UnitPriceInCents { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}

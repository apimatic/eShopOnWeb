using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = default!;
    public string ProductName { get; set; } = default!;
    public string State { get; set; } = default!;
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
}

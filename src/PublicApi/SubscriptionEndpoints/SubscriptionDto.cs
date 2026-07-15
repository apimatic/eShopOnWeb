using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public bool CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}

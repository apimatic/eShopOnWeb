using System;

namespace Microsoft.eShopWeb.PublicApi.Models.Subscription;

public class UserSubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string? ProductName { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
}

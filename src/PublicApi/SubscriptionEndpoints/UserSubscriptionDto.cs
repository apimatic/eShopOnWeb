using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class UserSubscriptionDto
{
    public long Id { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string State { get; set; } = null!;
    public decimal Price { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CancelledAt { get; set; }
}

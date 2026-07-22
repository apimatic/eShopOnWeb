using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class UsageDto
{
    public long Id { get; set; }
    public int SubscriptionId { get; set; }
    public string ComponentHandle { get; set; }
    public decimal Quantity { get; set; }
    public string Memo { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }
}

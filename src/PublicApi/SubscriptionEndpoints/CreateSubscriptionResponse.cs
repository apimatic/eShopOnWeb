using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public bool Success { get; set; }
    public long SubscriptionId { get; set; }
    public string? State { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public string? Error { get; set; }
}

using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    public bool AlreadySubscribed { get; set; }
    public long CustomerId { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public SubscriptionDto Subscription { get; set; } = new SubscriptionDto();
}

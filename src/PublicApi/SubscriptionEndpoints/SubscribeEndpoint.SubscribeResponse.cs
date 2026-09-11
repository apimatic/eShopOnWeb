using System;

namespace Microsoft.eShopWeb.PublicApi;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    public SubscriptionResult Subscription { get; set; } = new SubscriptionResult();
}

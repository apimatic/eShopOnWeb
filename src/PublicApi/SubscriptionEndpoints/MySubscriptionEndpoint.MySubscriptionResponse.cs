using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionResponse : BaseResponse
{
    public MySubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MySubscriptionResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}

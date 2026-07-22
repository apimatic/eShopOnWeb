using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionResponse : BaseResponse
{
    public GetSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetSubscriptionResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}

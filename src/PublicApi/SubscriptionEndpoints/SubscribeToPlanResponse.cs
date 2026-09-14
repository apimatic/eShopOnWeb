using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeToPlanResponse : BaseResponse
{
    public SubscribeToPlanResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeToPlanResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }

    public bool AlreadySubscribed { get; set; }
}

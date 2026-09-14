using System;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    public SubscriptionDetailsDto? Subscription { get; set; }
}

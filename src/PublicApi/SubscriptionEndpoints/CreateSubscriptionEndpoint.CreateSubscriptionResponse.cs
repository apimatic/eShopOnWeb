using System;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public ShopperSubscriptionDto? Subscription { get; set; }
    public bool Created { get; set; }
}

using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateShopperSubscriptionResponse : BaseResponse
{
    public CreateShopperSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateShopperSubscriptionResponse()
    {
    }

    public ShopperSubscriptionDto Subscription { get; set; } = new();
    public bool Created { get; set; }
}

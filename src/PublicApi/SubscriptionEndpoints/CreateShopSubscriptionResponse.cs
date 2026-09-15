using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateShopSubscriptionResponse : BaseResponse
{
    public CreateShopSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateShopSubscriptionResponse()
    {
    }

    public ShopSubscriptionDto Subscription { get; set; }
}

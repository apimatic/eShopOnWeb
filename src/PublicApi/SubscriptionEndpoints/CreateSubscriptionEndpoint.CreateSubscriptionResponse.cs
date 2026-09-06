using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse() : base(Guid.NewGuid())
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}

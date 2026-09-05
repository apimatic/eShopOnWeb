using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public SubscriptionDto? Subscription { get; set; }

    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }
}

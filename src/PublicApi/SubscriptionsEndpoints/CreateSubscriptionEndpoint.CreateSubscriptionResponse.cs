using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

/// <summary>Response for POST api/subscriptions.</summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = new SubscriptionDto();
}

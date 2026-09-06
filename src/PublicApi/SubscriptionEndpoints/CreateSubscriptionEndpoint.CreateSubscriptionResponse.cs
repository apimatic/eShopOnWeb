using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>The plan, price, state and next billing date now on file for the shopper.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the shopper already held this subscription, so nothing new was created.
    /// The response is a 200 in that case, and a 201 when the subscription was created.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

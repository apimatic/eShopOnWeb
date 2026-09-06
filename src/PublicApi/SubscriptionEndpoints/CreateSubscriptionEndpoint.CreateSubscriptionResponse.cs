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

    /// <summary>The resulting subscription: plan, price, state and next billing date.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when this request did not create anything because the shopper already held the
    /// subscription being returned. A repeated subscribe is reported, not rejected.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

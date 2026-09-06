using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    /// <summary>The shopper's subscription: plan, price, state and next billing date.</summary>
    public SubscriptionDto Subscription { get; set; }

    /// <summary>
    /// True when the shopper already held a live subscription to this plan, so nothing new was
    /// created and the subscription above is the existing one. A double-click sets this on the
    /// second response.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

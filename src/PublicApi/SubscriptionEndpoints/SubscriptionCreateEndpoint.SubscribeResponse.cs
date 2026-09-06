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

    /// <summary>The shopper's enrollment: plan, price, state and next billing date.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// False when the shopper was already subscribed to this plan and the existing enrollment was
    /// returned instead of a new one — the signal that a repeated request was absorbed.
    /// </summary>
    public bool Created { get; set; }
}

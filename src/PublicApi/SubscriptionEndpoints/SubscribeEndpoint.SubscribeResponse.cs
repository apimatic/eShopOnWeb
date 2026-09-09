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

    /// <summary>The resulting subscription (plan, price, state, next billing date).</summary>
    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the shopper was already subscribed to this plan and the existing subscription was returned
    /// (idempotent). False when a new subscription was created.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

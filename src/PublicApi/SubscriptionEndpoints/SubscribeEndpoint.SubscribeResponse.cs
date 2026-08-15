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

    /// <summary>The confirmed subscription (plan, price, state, next billing date).</summary>
    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>True when the shopper was already subscribed to this plan (idempotent hit).</summary>
    public bool AlreadyExisted { get; set; }
}

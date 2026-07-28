using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeSubscriptionResponse : BaseResponse
{
    public SubscribeSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeSubscriptionResponse()
    {
    }

    /// <summary>The active subscription (plan, price, state, next-billing date).</summary>
    public CustomerSubscriptionDto Subscription { get; set; } = new();

    /// <summary>
    /// True when the caller was already subscribed to this plan and no new subscription was created
    /// (idempotent replay — e.g. a double-click).
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

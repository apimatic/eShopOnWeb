using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    /// <summary>The subscription (plan, price, state, next billing date) as confirmed by Maxio.</summary>
    public CustomerSubscription? Subscription { get; set; }

    /// <summary>
    /// True when the caller already had a subscription for this plan and it was returned unchanged
    /// (idempotent hit — no second subscription was created).
    /// </summary>
    public bool AlreadySubscribed { get; set; }

    /// <summary>The Maxio customer id the subscription belongs to.</summary>
    public int CustomerId { get; set; }
}

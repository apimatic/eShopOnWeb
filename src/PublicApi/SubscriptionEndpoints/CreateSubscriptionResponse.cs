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

    /// <summary>The resulting subscription (created now, or already present).</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the subscription already existed for this plan (idempotent re-subscribe);
    /// false when this call created a brand-new subscription.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

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

    /// <summary>The subscription the shopper is enrolled in.</summary>
    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// <c>true</c> when the shopper was already enrolled in this plan and no new subscription was
    /// created (idempotent replay); <c>false</c> when this request created the subscription.
    /// </summary>
    public bool AlreadyExisted { get; set; }
}

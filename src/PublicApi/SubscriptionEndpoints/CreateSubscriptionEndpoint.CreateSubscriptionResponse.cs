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

    /// <summary>The shopper's subscription, whether it was created by this request or already existed.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the shopper was already subscribed to this plan and no new subscription was created.
    /// Repeating the request is safe and always reports the same subscription.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

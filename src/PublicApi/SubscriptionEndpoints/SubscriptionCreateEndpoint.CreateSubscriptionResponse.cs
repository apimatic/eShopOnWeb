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

    /// <summary>The shopper's subscription to the requested plan.</summary>
    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the shopper was already subscribed to this plan and no new subscription was created — a
    /// repeated request is answered with the existing subscription rather than a second charge.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

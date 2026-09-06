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

    public SubscriptionDto Subscription { get; set; }

    /// <summary>
    /// True when the shopper was already subscribed to this plan and the existing subscription is
    /// being returned, rather than a new one having been created.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

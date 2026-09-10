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

    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>
    /// True when the shopper was already subscribed to this plan and the existing subscription was
    /// returned (idempotent). False when this request created the subscription.
    /// </summary>
    public bool AlreadyExisted { get; set; }
}

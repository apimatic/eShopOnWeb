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

    /// <summary>
    /// False when the shopper was already subscribed to this plan and the existing subscription was
    /// returned unchanged. Subscribing is idempotent, so a repeated request reports false rather
    /// than creating a second subscription.
    /// </summary>
    public bool Created { get; set; }

    public SubscriptionDto? Subscription { get; set; }
}

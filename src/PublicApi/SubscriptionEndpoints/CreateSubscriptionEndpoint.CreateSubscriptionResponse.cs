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

    /// <summary>The active subscription (plan, price, state and next billing date).</summary>
    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>
    /// True when the shopper was already subscribed to this plan and the request was an idempotent no-op
    /// (e.g. a double-clicked subscribe). False when a new subscription was created.
    /// </summary>
    public bool AlreadyExisted { get; set; }
}

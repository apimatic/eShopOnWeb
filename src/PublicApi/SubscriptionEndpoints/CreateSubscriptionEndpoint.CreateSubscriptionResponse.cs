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

    /// <summary>The subscription the user now holds (plan, price, state, next billing date, ...).</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the user was already subscribed to this plan and the existing subscription was
    /// returned instead of creating a new one (idempotent re-subscribe / double-click).
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

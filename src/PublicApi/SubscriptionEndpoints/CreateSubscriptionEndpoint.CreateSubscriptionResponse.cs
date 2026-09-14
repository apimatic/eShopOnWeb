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

    /// <summary>The subscription the caller now holds (plan, price, state, next billing date).</summary>
    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the caller was already subscribed to this plan and the existing subscription was
    /// returned instead of creating a new one (idempotent subscribe).
    /// </summary>
    public bool AlreadyExisted { get; set; }
}

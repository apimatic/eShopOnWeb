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

    /// <summary>The subscription the caller now holds: plan, price, state and next billing date.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the caller was already enrolled in this plan and the existing subscription was
    /// returned instead of a second one being created.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

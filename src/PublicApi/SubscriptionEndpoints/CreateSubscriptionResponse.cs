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
    /// The subscription as recorded by the billing system (plan, price, state,
    /// next billing date).
    /// </summary>
    public SubscriptionDto Subscription { get; set; } = new SubscriptionDto();

    /// <summary>
    /// True when the user already held a live subscription on the plan and the
    /// existing one was returned instead of creating a duplicate.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}
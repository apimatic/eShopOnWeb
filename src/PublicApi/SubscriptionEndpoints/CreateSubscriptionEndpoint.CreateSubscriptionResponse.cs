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

    public SubscriptionDto Subscription { get; set; } = new SubscriptionDto();

    /// <summary>
    /// True when the shopper already held a live subscription for the plan and the
    /// existing one was returned (idempotent replay) instead of creating a new one.
    /// </summary>
    public bool AlreadyExisted { get; set; }
}

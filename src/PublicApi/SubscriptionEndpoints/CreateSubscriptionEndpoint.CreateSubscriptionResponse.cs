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

    /// <summary>The billing-system customer id the subscription belongs to.</summary>
    public int CustomerId { get; set; }

    /// <summary>
    /// True when an existing live subscription for this user+plan was returned instead of creating a
    /// new one (idempotent re-subscribe).
    /// </summary>
    public bool AlreadyExisted { get; set; }
}

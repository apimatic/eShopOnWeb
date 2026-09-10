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

    /// <summary>The active (or existing) subscription for the caller.</summary>
    public CustomerSubscriptionDto Subscription { get; set; } = new();

    /// <summary>
    /// True when a matching live subscription already existed and was returned unchanged
    /// (the subscribe call is idempotent).
    /// </summary>
    public bool AlreadyExisted { get; set; }
}

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

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>True only when this call is the one that created the subscription.</summary>
    public bool Created { get; set; }

    /// <summary>
    /// Why this subscription was returned: <c>created</c>, <c>already_subscribed</c> (the shopper
    /// already held a live subscription to the plan) or <c>idempotent_replay</c> (a previous request
    /// with the same idempotency key created it).
    /// </summary>
    public string Outcome { get; set; } = string.Empty;
}

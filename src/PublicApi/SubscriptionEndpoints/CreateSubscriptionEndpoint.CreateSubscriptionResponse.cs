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

    /// <summary>
    /// True when a live subscription to this plan already existed and was returned unchanged — the
    /// answer to a double-clicked Subscribe button. The response status says the same thing: 201 for a
    /// newly created subscription, 200 for a replay.
    /// </summary>
    public bool AlreadySubscribed { get; set; }

    /// <summary>True when this request also created the caller's billing customer record.</summary>
    public bool CustomerCreated { get; set; }
}

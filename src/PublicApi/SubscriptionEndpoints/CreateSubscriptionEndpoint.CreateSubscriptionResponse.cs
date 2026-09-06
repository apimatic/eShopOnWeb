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

    /// <summary>The live subscription the caller now holds.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the caller was already subscribed to this plan and nothing new was created.
    /// The endpoint answers 200 in that case and 201 when it enrolled the caller.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

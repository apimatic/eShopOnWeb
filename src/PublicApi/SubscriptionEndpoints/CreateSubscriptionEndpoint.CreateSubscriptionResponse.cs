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
    /// True when the caller was already actively subscribed to the plan and the existing
    /// subscription is returned unchanged (idempotent re-subscribe) rather than a new one created.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    /// <summary>
    /// True when the caller was already subscribed to this plan and the existing subscription was
    /// returned unchanged (idempotent path), false when a new subscription was created.
    /// </summary>
    public bool AlreadyExisted { get; set; }

    public CustomerSubscriptionDto Subscription { get; set; } = new();
}

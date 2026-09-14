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

    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the subscriber already had a live subscription to the plan and it was
    /// returned unchanged (idempotent no-op) rather than a new one being created.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

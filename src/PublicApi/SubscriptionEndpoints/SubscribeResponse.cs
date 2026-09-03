using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }

    public SubscribeResponse() { }

    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>True when the subscriber already had a live subscription to the plan (idempotent no-op).</summary>
    public bool AlreadySubscribed { get; set; }
}

using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }

    public SubscribeResponse() { }

    /// <summary>The active subscription (newly created or pre-existing).</summary>
    public CustomerSubscriptionDto Subscription { get; set; } = new();

    /// <summary>
    /// True when the user already had a live subscription to this plan and no new
    /// subscription was created (idempotent no-op).
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

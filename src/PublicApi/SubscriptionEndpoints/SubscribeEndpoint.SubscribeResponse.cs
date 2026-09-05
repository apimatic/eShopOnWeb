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

    public UserSubscriptionDto Subscription { get; set; } = new();

    /// <summary>
    /// True when the caller already had a current subscription to this plan and no new Maxio
    /// subscription was created (the idempotent, double-click-safe path).
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

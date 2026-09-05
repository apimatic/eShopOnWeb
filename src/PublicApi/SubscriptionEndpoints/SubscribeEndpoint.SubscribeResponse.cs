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

    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>False when the caller already had a live subscription to this plan and it was returned as-is.</summary>
    public bool IsNewSubscription { get; set; }
}

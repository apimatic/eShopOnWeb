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

    /// <summary>True when this call created the subscription; false when the user already had a live subscription to the same plan (idempotent replay).</summary>
    public bool Created { get; set; }

    public SubscriptionDto Subscription { get; set; } = new SubscriptionDto();
}

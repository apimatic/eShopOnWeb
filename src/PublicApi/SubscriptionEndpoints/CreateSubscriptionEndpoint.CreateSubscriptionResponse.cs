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

    /// <summary>
    /// True when this call created the subscription; false when the caller already
    /// held a live subscription to the requested plan (idempotent re-subscribe).
    /// </summary>
    public bool Created { get; set; }

    public SubscriptionItemDto? Subscription { get; set; }
}

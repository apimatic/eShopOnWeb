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

    /// <summary>False when this call created a brand-new subscription; true when the buyer was already enrolled in this plan.</summary>
    public bool AlreadySubscribed { get; set; }

    public SubscriptionDto Subscription { get; set; } = default!;
}

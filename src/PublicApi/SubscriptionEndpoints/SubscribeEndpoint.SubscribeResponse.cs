using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse()
    {
    }

    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    /// <summary>True when this call created a new subscription; false when an existing live subscription was returned.</summary>
    public bool SubscriptionCreated { get; set; }

    public SubscriptionDto Subscription { get; set; } = new();
}

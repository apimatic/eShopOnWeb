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

    /// <summary>True when this request created a new subscription; false when the user was already subscribed to the plan.</summary>
    public bool Created { get; set; }

    public CustomerSubscriptionDto? Subscription { get; set; }
}

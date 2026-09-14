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

    /// <summary>The shopper's subscription (newly created or the pre-existing active one).</summary>
    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>True when an active subscription for the plan already existed and was returned.</summary>
    public bool AlreadyExisted { get; set; }
}

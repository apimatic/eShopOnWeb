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

    /// <summary>
    /// True when this call enrolled the shopper. False when an equivalent live subscription already existed
    /// and was returned instead, which is what a repeated submit produces.
    /// </summary>
    public bool Created { get; set; }

    /// <summary>The shopper's subscription to the requested plan.</summary>
    public SubscriptionDto? Subscription { get; set; }
}

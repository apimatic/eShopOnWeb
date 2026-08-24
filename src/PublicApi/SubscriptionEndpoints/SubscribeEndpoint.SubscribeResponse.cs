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

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>True when an equivalent live subscription already existed and was returned instead of creating a duplicate.</summary>
    public bool AlreadyExisted { get; set; }
}

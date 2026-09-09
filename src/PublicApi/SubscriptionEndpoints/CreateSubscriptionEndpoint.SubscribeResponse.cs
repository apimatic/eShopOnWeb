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

    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the caller was already enrolled in this plan and the existing subscription was
    /// returned instead of creating a duplicate.
    /// </summary>
    public bool AlreadyExisted { get; set; }
}

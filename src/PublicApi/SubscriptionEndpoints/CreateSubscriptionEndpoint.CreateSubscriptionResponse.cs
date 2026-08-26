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

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the caller already had a non-terminal subscription to this plan and the
    /// existing one is returned instead of creating a duplicate.
    /// </summary>
    public bool AlreadyExisted { get; set; }
}

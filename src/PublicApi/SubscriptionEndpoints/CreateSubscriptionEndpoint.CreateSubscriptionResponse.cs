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

    public SubscriptionDto Subscription { get; set; } = new SubscriptionDto();

    /// <summary>True when nothing new was created because the shopper already holds a live
    /// subscription to this plan (e.g. a repeated or double-clicked request).</summary>
    public bool AlreadyExisted { get; set; }
}

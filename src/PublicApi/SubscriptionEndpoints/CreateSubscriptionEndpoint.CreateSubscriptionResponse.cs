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

    public SubscriptionDto Subscription { get; set; }

    /// <summary>
    /// True when the subscription already existed for this shopper + plan (idempotent repeat of a
    /// successful subscribe); false when this call created it.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

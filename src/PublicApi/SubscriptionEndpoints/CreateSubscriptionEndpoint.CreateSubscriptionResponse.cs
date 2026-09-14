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

    /// <summary>The subscription the shopper now holds.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the shopper already held this subscription, so nothing new was created. A repeated
    /// request - a double-click, or a retry - reports the same subscription with this flag set.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

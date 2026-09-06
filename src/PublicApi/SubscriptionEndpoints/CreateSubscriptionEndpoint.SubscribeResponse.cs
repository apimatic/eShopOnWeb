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

    /// <summary>The shopper's subscription to the requested plan.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the shopper was already enrolled on this plan and no new subscription was created.
    /// A repeated request — a double-click, a client retry — reports <c>true</c> rather than enrolling twice.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

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

    /// <summary>
    /// True when the caller already held this subscription and nothing new was created. A repeated
    /// request — a double-click, a client retry — reports the same subscription with this flag set.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

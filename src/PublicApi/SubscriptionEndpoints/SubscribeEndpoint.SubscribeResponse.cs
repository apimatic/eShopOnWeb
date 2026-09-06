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
    /// True when the caller already had a live subscription to this plan, so nothing new was created and the
    /// existing subscription is returned instead. This is the expected result of a double click.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

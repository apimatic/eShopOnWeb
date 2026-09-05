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
    /// True when an existing subscription to this plan was returned instead of creating a
    /// new one (e.g. a double-submitted request).
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

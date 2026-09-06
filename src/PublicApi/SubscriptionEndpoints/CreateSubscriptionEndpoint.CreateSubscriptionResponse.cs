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
    /// True when the shopper was already subscribed to this plan and the existing subscription was
    /// returned instead of a second one being created. A repeated (double-clicked or retried)
    /// subscribe is answered with 200 and this flag rather than 201.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

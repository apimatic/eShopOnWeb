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
    /// True when the request resolved to a subscription that already existed, so nothing new was
    /// billed. The endpoint answers 200 OK in that case and 201 Created for a fresh enrollment.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

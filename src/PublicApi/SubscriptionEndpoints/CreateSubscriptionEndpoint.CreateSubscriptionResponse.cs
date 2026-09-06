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

    /// <summary>The subscription the shopper now holds, including its next billing date.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the shopper was already enrolled on this plan and nothing new was created. The
    /// response is <c>200 OK</c> in that case and <c>201 Created</c> for a fresh enrollment.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

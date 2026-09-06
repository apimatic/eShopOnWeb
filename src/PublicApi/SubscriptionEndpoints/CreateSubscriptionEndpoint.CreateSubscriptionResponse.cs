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

    /// <summary>
    /// True when this request enrolled the shopper. False when the shopper was already subscribed to
    /// the plan and the existing subscription was returned instead.
    /// </summary>
    public bool Created { get; set; }

    public SubscriptionDto? Subscription { get; set; }
}

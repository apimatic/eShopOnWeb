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
    /// <c>true</c> when this request enrolled the shopper. <c>false</c> when an equivalent live
    /// subscription already existed and was returned instead — the idempotent path a repeated request takes.
    /// </summary>
    public bool Created { get; set; }
}

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

    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>
    /// <c>true</c> when the subscription was created by this request; <c>false</c> when the
    /// shopper already held an open subscription to the plan and the existing one is returned.
    /// </summary>
    public bool Created { get; set; }
}

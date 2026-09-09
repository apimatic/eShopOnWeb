using System;
using Microsoft.eShopWeb.PublicApi;

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
    /// True when a new subscription was created; false when the shopper was already
    /// subscribed to the plan and the existing subscription is returned.
    /// </summary>
    public bool Created { get; set; }

    public SubscriptionDto Subscription { get; set; } = new SubscriptionDto();
}

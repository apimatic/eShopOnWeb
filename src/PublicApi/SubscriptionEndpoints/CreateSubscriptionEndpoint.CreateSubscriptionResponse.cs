using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId)
        : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>
    /// True when the subscription was just created; false when an existing
    /// subscription to the same plan was returned (idempotent re-subscribe).
    /// </summary>
    public bool Created { get; set; }

    public SubscriptionDto Subscription { get; set; } = new();
}

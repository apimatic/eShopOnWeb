using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse()
    {
    }

    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>
    /// True when this call actually enrolled the subscription; false when an existing
    /// subscription was returned (idempotent replay of an earlier request).
    /// </summary>
    public bool NewlyCreated { get; set; }
}

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

    /// <summary>The subscription as recorded in Maxio Advanced Billing.</summary>
    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>
    /// True when this call created the subscription; false when an existing
    /// subscription was returned (idempotent replay of a prior subscribe call).
    /// </summary>
    public bool Created { get; set; }
}

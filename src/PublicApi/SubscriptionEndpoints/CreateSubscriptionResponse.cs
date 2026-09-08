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
    /// True when a new subscription was created; false when an existing, still-valid
    /// subscription was returned (idempotent subscribe).
    /// </summary>
    public bool WasCreated { get; set; }

    public SubscriptionDto? Subscription { get; set; }
}

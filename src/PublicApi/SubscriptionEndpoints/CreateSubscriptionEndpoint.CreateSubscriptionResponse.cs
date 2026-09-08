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

    /// <summary>The subscription as confirmed by Maxio.</summary>
    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>
    /// True when Maxio created a new subscription; false when an existing subscription to
    /// the requested plan was returned (idempotent replay of a duplicate request).
    /// </summary>
    public bool WasCreated { get; set; }
}

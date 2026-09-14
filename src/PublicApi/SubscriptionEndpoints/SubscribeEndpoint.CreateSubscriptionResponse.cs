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
    /// True when the shopper was already actively enrolled in this plan and no new
    /// subscription was created (the subscribe flow is idempotent).
    /// </summary>
    public bool AlreadySubscribed { get; set; }

    public SubscriptionDto Subscription { get; set; } = null!;
}

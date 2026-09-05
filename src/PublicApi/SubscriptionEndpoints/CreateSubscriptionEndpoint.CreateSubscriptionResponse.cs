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

    public SubscriptionDto Subscription { get; set; } = new SubscriptionDto();

    /// <summary>
    /// True when the buyer already had a live subscription to this plan and no new
    /// subscription was created (idempotent replay of a repeated/double-click request).
    /// </summary>
    public bool AlreadyEnrolled { get; set; }
}

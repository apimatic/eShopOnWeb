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

    /// <summary>The resulting subscription (plan, price, state, next billing date).</summary>
    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>
    /// True when the shopper was already enrolled in the plan and the existing subscription
    /// was returned instead of a new one being created (idempotent outcome).
    /// </summary>
    public bool AlreadyEnrolled { get; set; }
}

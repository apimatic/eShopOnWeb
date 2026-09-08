using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response for <c>POST /api/subscriptions</c>. Confirms the resulting plan, price, state and
/// next billing date back to the shopper.
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>The subscription that is now in force.</summary>
    public SubscriptionDto Subscription { get; set; } = new SubscriptionDto();

    /// <summary>
    /// True when the subscription was created by this request; false when an existing live
    /// subscription for the same plan was returned (idempotent re-subscribe).
    /// </summary>
    public bool WasCreated { get; set; }
}

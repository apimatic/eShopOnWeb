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

    /// <summary>The shopper's enrolment: plan, price, state and next billing date.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when this request created the subscription. False when the shopper was already
    /// subscribed to the plan and the existing subscription was returned instead — a repeated or
    /// double-clicked request lands here rather than creating a second subscription.
    /// </summary>
    public bool Created { get; set; }
}

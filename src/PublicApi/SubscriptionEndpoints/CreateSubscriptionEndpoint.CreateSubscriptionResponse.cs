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

    /// <summary>The shopper's subscription, including its state and next billing date.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>The plan that was subscribed to, including its price and billing interval.</summary>
    public SubscriptionPlanDto? Plan { get; set; }

    /// <summary>
    /// True when the shopper already had a live subscription to this plan and nothing new was
    /// created - the idempotent outcome of a repeated request.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

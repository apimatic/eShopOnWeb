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

    /// <summary>The subscription as confirmed by the billing system: plan, price, state, next bill date.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>The plan that was subscribed to.</summary>
    public SubscriptionPlanDto? Plan { get; set; }

    /// <summary>
    /// True when the shopper already had a live subscription to this plan, so the existing one was
    /// returned rather than a second one being created.
    /// </summary>
    public bool AlreadySubscribed { get; set; }

    /// <summary>A short confirmation suitable for showing to the shopper.</summary>
    public string Message { get; set; } = string.Empty;
}

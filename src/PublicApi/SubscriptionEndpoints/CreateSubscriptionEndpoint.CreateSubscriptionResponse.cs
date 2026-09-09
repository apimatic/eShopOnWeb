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

    /// <summary>The active (or newly created) subscription.</summary>
    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the shopper was already subscribed to this plan and the existing subscription was
    /// returned instead of creating a duplicate.
    /// </summary>
    public bool AlreadyExisted { get; set; }

    /// <summary>Human-readable confirmation summarizing plan, price, state and next billing date.</summary>
    public string? Message { get; set; }
}

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

    /// <summary>The shopper's enrollment: plan, price, state and next billing date.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>The plan as it currently stands in the billing catalog.</summary>
    public SubscriptionPlanDto? Plan { get; set; }

    /// <summary>
    /// True when a live subscription to this plan already existed and was returned unchanged.
    /// A double-clicked subscribe produces this instead of a second subscription.
    /// </summary>
    public bool AlreadySubscribed { get; set; }

    /// <summary>The billing system's customer id for the shopper.</summary>
    public long CustomerId { get; set; }

    /// <summary>The deterministic customer reference this application owns in the billing system.</summary>
    public string? CustomerReference { get; set; }
}

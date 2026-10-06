using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response confirming the subscription, with plan, price, state and next billing date.
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse() : base()
    {
    }

    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>False when the shopper was already subscribed; the existing subscription is returned unchanged.</summary>
    public bool WasNewlyCreated { get; set; }
}

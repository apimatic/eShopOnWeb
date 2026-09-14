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

    /// <summary>The shopper's subscription, whether it was just created or already existed.</summary>
    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the shopper was already enrolled and this call returned the existing
    /// subscription (a repeated click, a retry, or a replayed idempotency key).
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

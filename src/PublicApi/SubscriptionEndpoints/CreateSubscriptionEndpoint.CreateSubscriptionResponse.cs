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

    /// <summary>The resulting subscription: plan, price, state and next billing date.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the shopper was already enrolled and this call returned the existing
    /// subscription rather than creating another one. Responses with this flag set are 200 OK;
    /// a freshly created subscription is 201 Created.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public CreateSubscriptionResponse() { }

    /// <summary>The resulting subscription: plan, price, state and next billing date.</summary>
    public SubscriptionDto? Subscription { get; set; }
}

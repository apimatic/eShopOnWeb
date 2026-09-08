using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response confirming a subscription enrollment (plan, price, state, next billing date).
/// </summary>
public class SubscriptionCreateResponse : BaseResponse
{
    public SubscriptionCreateResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionCreateResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}

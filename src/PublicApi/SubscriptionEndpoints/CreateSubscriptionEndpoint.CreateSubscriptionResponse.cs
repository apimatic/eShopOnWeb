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

    /// <summary>
    /// True when this call enrolled the shopper. False when an equivalent subscription already
    /// existed and was returned instead - the idempotent path, answered with <c>200 OK</c>.
    /// </summary>
    public bool Created { get; set; }

    /// <summary>The subscription the shopper now holds.</summary>
    public SubscriptionDto? Subscription { get; set; }
}

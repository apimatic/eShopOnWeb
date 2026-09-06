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

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>False when the shopper was already subscribed and this request changed nothing.</summary>
    public bool Created { get; set; }

    /// <summary>True when this request also created the shopper's billing customer record.</summary>
    public bool CustomerCreated { get; set; }
}

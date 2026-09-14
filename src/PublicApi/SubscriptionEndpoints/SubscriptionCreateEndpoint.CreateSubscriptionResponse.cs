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
    /// False when the caller was already subscribed and the existing subscription is being returned
    /// unchanged - the shopper has not been enrolled or charged twice.
    /// </summary>
    public bool Created { get; set; }

    public SubscriptionDto? Subscription { get; set; }
}

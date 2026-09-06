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

    /// <summary>The shopper's subscription, whether this call created it or it already existed.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// <c>true</c> when this call created the subscription; <c>false</c> when an equivalent
    /// enrollment already existed and was returned unchanged.
    /// </summary>
    public bool Created { get; set; }
}

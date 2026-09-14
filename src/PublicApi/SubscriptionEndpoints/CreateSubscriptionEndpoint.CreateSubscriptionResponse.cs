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

    /// <summary>The subscription, confirmed back from the billing system.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// What the call did: <c>created</c>, <c>alreadySubscribed</c> when the shopper already held
    /// this plan, or <c>idempotentReplay</c> when an earlier request with the same key made it.
    /// </summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>True only when this call is what brought the subscription into existence.</summary>
    public bool Created { get; set; }
}

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

    /// <summary>The subscription as the billing system now holds it.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// False when the shopper was already subscribed to this plan and the existing subscription
    /// was returned instead of a second one being created.
    /// </summary>
    public bool Created { get; set; }

    /// <summary>True when this call also created the shopper's customer record in the billing system.</summary>
    public bool CustomerCreated { get; set; }
}

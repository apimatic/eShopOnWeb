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

    /// <summary>The subscription the shopper now holds.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when this call enrolled the shopper. False when they were already subscribed to the
    /// plan and the existing subscription was returned instead - a repeated or double-clicked
    /// subscribe never creates a second subscription.
    /// </summary>
    public bool Created { get; set; }
}

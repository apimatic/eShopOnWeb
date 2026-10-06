using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response confirming the shopper's subscription.
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the shopper was already enrolled in this plan; the call was
    /// a replay (e.g. a double-click) and no second subscription was created.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

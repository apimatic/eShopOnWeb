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

    /// <summary>The subscription the caller now holds, straight from the billing system of record.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the caller already held a live subscription to this plan and no new one was created.
    /// A repeated or double-clicked request returns the original subscription with this flag set.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

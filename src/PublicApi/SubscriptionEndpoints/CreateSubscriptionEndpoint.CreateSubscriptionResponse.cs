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

    /// <summary>
    /// True when the caller already held this subscription and nothing was created — the answer a
    /// repeated or double-clicked request gets.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

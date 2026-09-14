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
    /// True when the user already had a live subscription to the requested plan and no new
    /// subscription was created (idempotent response to a repeated/double-clicked request).
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

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

    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>
    /// True when the caller was already subscribed to the requested plan and no new
    /// Maxio subscription was created (idempotent re-subscribe).
    /// </summary>
    public bool WasAlreadySubscribed { get; set; }
}

using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    /// <summary>The subscription as reflected in Maxio (plan, price, state, next-billing date).</summary>
    public CustomerSubscriptionDto Subscription { get; set; } = new();

    /// <summary>
    /// True when an equivalent active subscription already existed and no new one was created
    /// (idempotent — a double-click does not create a second subscription).
    /// </summary>
    public bool AlreadyExisted { get; set; }
}

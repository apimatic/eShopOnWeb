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
    /// True when the shopper already had a live subscription to this plan and this call
    /// returned it instead of creating a duplicate (the idempotent "double-click" case).
    /// </summary>
    public bool AlreadyExisted { get; set; }
}

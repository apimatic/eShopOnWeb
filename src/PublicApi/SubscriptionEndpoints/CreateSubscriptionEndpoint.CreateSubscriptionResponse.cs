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

    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the user already had a live subscription to this plan and no new
    /// subscription was created (idempotent hit — e.g. a double-click).
    /// </summary>
    public bool AlreadyExisted { get; set; }
}

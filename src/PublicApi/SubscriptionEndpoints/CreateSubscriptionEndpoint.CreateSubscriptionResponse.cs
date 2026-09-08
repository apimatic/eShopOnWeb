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
    /// True when an existing subscription for this (user, plan) was returned instead of creating a new
    /// one — the idempotency signal that makes a repeated request (e.g. a double-click) safe.
    /// </summary>
    public bool AlreadyExisted { get; set; }
}

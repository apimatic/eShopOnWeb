using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public CreateSubscriptionResponse() { }

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when an active subscription to the requested plan already existed (e.g. a double-click),
    /// in which case that existing subscription is returned instead of a new one.
    /// </summary>
    public bool AlreadyExisted { get; set; }
}

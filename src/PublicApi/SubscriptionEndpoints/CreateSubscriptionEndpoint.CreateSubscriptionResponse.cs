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
    /// True when an existing live subscription to this plan was returned instead of a new
    /// one being created (e.g. a double-click resubmission of the same request).
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

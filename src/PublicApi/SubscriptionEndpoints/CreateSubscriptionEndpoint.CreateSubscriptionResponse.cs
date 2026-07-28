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

    public CustomerSubscriptionDto Subscription { get; set; } = new();

    /// <summary>True when a live subscription to this plan already existed and was returned unchanged.</summary>
    public bool AlreadyExisted { get; set; }
}

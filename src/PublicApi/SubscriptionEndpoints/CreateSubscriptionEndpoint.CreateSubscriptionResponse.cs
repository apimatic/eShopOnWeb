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
    /// True when a matching live subscription already existed and was reused (idempotent enrollment).
    /// </summary>
    public bool AlreadyExisted { get; set; }
}

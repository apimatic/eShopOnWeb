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
    /// True when the shopper already held a live subscription to the plan and the existing
    /// subscription was returned (nothing new was created).
    /// </summary>
    public bool AlreadyExisted { get; set; }

    public string? ErrorMessage { get; set; }
}

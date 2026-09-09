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

    /// <summary>
    /// False when the user already held an active subscription to the requested plan and the
    /// existing subscription was returned instead of creating a duplicate.
    /// </summary>
    public bool Created { get; set; }

    public SubscriptionDto? Subscription { get; set; }
}

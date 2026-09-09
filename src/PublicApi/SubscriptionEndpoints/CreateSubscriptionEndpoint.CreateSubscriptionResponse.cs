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
    /// False when a new subscription was created; true when the user already had a live
    /// subscription to this plan and the existing one was returned (idempotent replay).
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

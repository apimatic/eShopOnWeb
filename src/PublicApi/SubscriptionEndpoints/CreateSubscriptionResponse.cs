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
    /// True when the shopper was already subscribed to this plan and the existing subscription
    /// was returned instead of creating a duplicate (idempotent replay of a subscribe request).
    /// </summary>
    public bool AlreadyExisted { get; set; }
}

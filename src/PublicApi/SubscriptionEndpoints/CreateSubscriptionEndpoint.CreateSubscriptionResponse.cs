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

    /// <summary>The confirmed subscription (plan, price, state, next billing date).</summary>
    public CustomerSubscriptionDto Subscription { get; set; } = new();

    /// <summary>
    /// True when an active subscription for this plan already existed and was returned
    /// instead of creating a duplicate (idempotent subscribe).
    /// </summary>
    public bool AlreadyExisted { get; set; }

    public string Message { get; set; } = string.Empty;
}

using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    /// <summary>The resulting subscription (plan, price, state and next billing date).</summary>
    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the subscriber was already enrolled and the existing subscription was returned
    /// (idempotent subscribe) rather than a new one being created.
    /// </summary>
    public bool AlreadyExisted { get; set; }

    public string Message { get; set; } = string.Empty;
}

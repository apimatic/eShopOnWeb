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
    /// True when this call created the subscription; false when the caller already had a
    /// subscription to the requested plan and the existing one was returned instead.
    /// </summary>
    public bool WasCreated { get; set; }
}

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

    public MySubscriptionDto? Subscription { get; set; }

    /// <summary>False when an existing subscription to this plan was returned instead of creating a new one.</summary>
    public bool WasNewlyCreated { get; set; }
}

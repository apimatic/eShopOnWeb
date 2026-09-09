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

    /// <summary>The active (or newly created) subscription, confirming plan, price, state and next billing date.</summary>
    public MySubscriptionDto Subscription { get; set; } = new();

    /// <summary>True when the user was already subscribed to this plan and no new subscription was created.</summary>
    public bool AlreadyExisted { get; set; }

    public string Message { get; set; } = string.Empty;
}

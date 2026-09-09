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

    /// <summary>The resulting subscription: plan, price, state and next billing date.</summary>
    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>True when the user was already subscribed to this plan and the existing subscription was returned.</summary>
    public bool AlreadySubscribed { get; set; }

    /// <summary>A human-readable confirmation message.</summary>
    public string Message { get; set; } = string.Empty;
}

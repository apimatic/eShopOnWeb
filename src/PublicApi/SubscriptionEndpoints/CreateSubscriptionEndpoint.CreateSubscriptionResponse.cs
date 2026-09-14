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

    /// <summary>The shopper's subscription, whether it was just created or already existed.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>The plan the subscription is on.</summary>
    public SubscriptionPlanDto? Plan { get; set; }

    /// <summary>True when the shopper was already enrolled and no new subscription was created.</summary>
    public bool AlreadySubscribed { get; set; }
}

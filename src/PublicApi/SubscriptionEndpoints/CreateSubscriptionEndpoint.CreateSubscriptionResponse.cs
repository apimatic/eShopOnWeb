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

    public SubscriptionDto Subscription { get; set; } = new SubscriptionDto();

    /// <summary>True when the shopper already held this plan and no new subscription was created.</summary>
    public bool AlreadySubscribed { get; set; }
}

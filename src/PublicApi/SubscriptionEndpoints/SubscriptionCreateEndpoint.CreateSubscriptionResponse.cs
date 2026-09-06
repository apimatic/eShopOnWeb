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

    /// <summary>
    /// True when the caller was already subscribed to this plan and nothing new was created.
    /// The response is then 200 rather than 201.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

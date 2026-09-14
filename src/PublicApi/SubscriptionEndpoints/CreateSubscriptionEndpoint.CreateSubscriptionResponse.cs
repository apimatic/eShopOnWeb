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
    /// True when the shopper was already enrolled in this plan and the existing subscription was
    /// returned unchanged — the double-click / retry path. No second subscription was created.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

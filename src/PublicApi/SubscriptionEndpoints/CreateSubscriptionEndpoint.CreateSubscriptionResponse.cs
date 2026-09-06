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
    /// True when the caller was already enrolled on this plan and the existing subscription was returned
    /// unchanged. A repeated POST is safe and lands here rather than creating a second subscription.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

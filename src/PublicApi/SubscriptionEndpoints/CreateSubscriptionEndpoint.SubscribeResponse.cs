using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeApiResponse : BaseResponse
{
    public SubscribeApiResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeApiResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }

    public SubscriptionPlanDto? Plan { get; set; }

    /// <summary>
    /// True when the shopper already had a live subscription to this plan, so the existing one was
    /// returned instead of a second being created.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}

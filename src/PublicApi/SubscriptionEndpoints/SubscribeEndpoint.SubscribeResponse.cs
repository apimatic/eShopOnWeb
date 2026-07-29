using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    /// <summary>The subscription the shopper is enrolled in (newly created or pre-existing).</summary>
    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the shopper already had a live subscription to this plan, so no new subscription
    /// was created (the request was idempotent).
    /// </summary>
    public bool AlreadySubscribed { get; set; }

    public string Message { get; set; } = string.Empty;
}

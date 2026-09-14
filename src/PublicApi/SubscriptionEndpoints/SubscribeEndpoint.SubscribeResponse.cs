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

    /// <summary>The active subscription for the caller (newly created or pre-existing).</summary>
    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// <c>true</c> when the caller was already subscribed to this plan and the existing subscription
    /// was returned instead of creating a duplicate.
    /// </summary>
    public bool AlreadySubscribed { get; set; }

    /// <summary>The Maxio customer id backing the caller.</summary>
    public int MaxioCustomerId { get; set; }
}

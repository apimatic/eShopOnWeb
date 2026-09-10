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

    /// <summary>The active subscription (newly created, or the shopper's existing one).</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>True when the shopper was already subscribed to this plan and no new subscription was created.</summary>
    public bool AlreadySubscribed { get; set; }

    /// <summary>A short confirmation message describing the outcome.</summary>
    public string Message { get; set; } = string.Empty;
}

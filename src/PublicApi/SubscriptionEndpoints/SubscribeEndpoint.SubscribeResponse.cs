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

    /// <summary>The resulting subscription (newly created, or the pre-existing one).</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>True when the caller was already subscribed to this plan and no new subscription was created.</summary>
    public bool AlreadySubscribed { get; set; }

    /// <summary>The Maxio customer id the subscription belongs to.</summary>
    public int CustomerId { get; set; }

    /// <summary>A human-readable confirmation message.</summary>
    public string Message { get; set; } = string.Empty;
}

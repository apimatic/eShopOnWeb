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

    /// <summary>The active subscription (newly created, or the pre-existing one when already subscribed).</summary>
    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>True when a live subscription to this plan already existed, so nothing new was created.</summary>
    public bool AlreadySubscribed { get; set; }

    /// <summary>Human-readable outcome, or an error description on failure.</summary>
    public string? Message { get; set; }
}

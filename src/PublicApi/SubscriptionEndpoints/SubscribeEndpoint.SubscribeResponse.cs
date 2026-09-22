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

    /// <summary>The Maxio customer id the subscription belongs to.</summary>
    public int CustomerId { get; set; }

    /// <summary>True when the user already had a live subscription and it was returned unchanged.</summary>
    public bool AlreadySubscribed { get; set; }

    public CustomerSubscriptionDto? Subscription { get; set; }
}

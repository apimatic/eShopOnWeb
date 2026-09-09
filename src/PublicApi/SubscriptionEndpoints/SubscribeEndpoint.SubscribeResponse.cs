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

    /// <summary>The active subscription (newly created or the pre-existing one).</summary>
    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>Billing-system customer id the subscription belongs to.</summary>
    public long CustomerId { get; set; }

    /// <summary>True when the caller was already subscribed and the existing subscription was returned.</summary>
    public bool AlreadyExisted { get; set; }

    /// <summary>Human-readable confirmation message.</summary>
    public string Message { get; set; } = string.Empty;
}

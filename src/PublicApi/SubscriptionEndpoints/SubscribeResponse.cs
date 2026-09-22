using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }

    public SubscribeResponse() { }

    public MySubscriptionDto Subscription { get; set; } = new();
    public int CustomerId { get; set; }

    /// <summary>True when the buyer was already enrolled — an idempotent repeat, no new provider write was made.</summary>
    public bool AlreadySubscribed { get; set; }
}

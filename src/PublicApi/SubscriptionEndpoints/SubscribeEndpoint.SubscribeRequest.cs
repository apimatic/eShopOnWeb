using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional client-generated idempotency key (e.g. a UUID created once per signup form view).
    /// Two POSTs sharing a key can never create two subscriptions; double-clicks without a key are
    /// still de-duplicated for the current minute.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Resolved from the JWT identity by the route (never bound from the request body).
    /// </summary>
    internal Microsoft.eShopWeb.ApplicationCore.Integration.Maxio.SubscriberProfile? Subscriber { get; set; }
}

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    public MySubscriptionDto Subscription { get; set; } = new();

    /// <summary>True when the shopper was already enrolled in this plan and no new subscription was created.</summary>
    public bool AlreadySubscribed { get; set; }
}

using System;
using Microsoft.eShopWeb.PublicApi.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Response for POST /api/subscriptions.</summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>The subscription that is now in effect for the caller.</summary>
    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>
    /// True when a new subscription was created; false when the caller was already subscribed
    /// to this plan (idempotent repeat of the same request).
    /// </summary>
    public bool Created { get; set; }
}

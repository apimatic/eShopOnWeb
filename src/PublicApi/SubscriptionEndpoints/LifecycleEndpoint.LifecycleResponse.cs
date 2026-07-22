using System;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleResponse : BaseResponse
{
    public LifecycleResponse(Guid correlationId) : base(correlationId)
    {
    }

    public LifecycleResponse()
    {
    }

    public SubscriptionLifecycleAction Action { get; set; }
    public string State { get; set; } = string.Empty;

    /// <summary>When the transition takes effect - now, or the period boundary for a deferred cancel.</summary>
    public DateTimeOffset? EffectiveAt { get; set; }

    public SubscriptionDto Subscription { get; set; } = new();
}

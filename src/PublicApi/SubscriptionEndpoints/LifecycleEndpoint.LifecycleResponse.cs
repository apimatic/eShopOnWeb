using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleResponse : BaseResponse
{
    public LifecycleResponse(Guid correlationId) : base(correlationId)
    {
    }

    public LifecycleResponse()
    {
    }

    public string Action { get; set; } = string.Empty;

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>When the transition takes effect, for a deferred cancellation.</summary>
    public DateTimeOffset? EffectiveAt { get; set; }
}

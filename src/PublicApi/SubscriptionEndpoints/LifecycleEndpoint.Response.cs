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

    public string Action { get; set; }

    /// <summary>The state the subscription was in before the transition was applied.</summary>
    public string PreviousStatus { get; set; }

    /// <summary>When the transition takes effect, which for an end-of-period cancel is in the future.</summary>
    public DateTimeOffset? EffectiveAt { get; set; }

    public SubscriptionDto Subscription { get; set; }
}

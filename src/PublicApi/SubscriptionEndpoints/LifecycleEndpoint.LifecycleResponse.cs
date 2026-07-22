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

    public SubscriptionDto? Subscription { get; set; }
    public string Action { get; set; } = string.Empty;
    public string PreviousState { get; set; } = string.Empty;
    public string NewState { get; set; } = string.Empty;

    /// <summary>When the transition takes effect. Null means it already has.</summary>
    public DateTimeOffset? EffectiveAt { get; set; }

    /// <summary>Set when the provider's outcome differs from what was requested.</summary>
    public string? Message { get; set; }
}

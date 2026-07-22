using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public enum LifecycleAction
{
    Pause = 0,
    Resume,
    Cancel,
    Reactivate
}

public class LifecycleRequest : BaseRequest
{
    public LifecycleAction Action { get; set; }

    /// <summary>Only meaningful for <see cref="LifecycleAction.Cancel"/>.</summary>
    public CancellationTiming Timing { get; set; }

    public string? Reason { get; set; }

    /// <summary>Only meaningful for <see cref="LifecycleAction.Pause"/>.</summary>
    public DateTimeOffset? AutomaticallyResumeAt { get; set; }

    /// <summary>Set from the route, never from the request body.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>Set from the bearer token; <c>null</c> for administrators.</summary>
    public string? OwnerBuyerId { get; set; }
}

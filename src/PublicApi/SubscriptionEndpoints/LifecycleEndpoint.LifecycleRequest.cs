using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>The four lifecycle transitions a subscription supports (UC4).</summary>
public enum SubscriptionLifecycleAction
{
    Pause = 0,
    Resume = 1,
    Cancel = 2,
    Reactivate = 3
}

public class LifecycleRequest : BaseRequest
{
    /// <summary>One of "Pause", "Resume", "Cancel" or "Reactivate".</summary>
    public SubscriptionLifecycleAction Action { get; set; }

    /// <summary>For a cancel: "Immediate" or "EndOfPeriod". Ignored by the other actions.</summary>
    public CancellationTiming Timing { get; set; } = CancellationTiming.Immediate;

    /// <summary>An optional reason recorded with the transition.</summary>
    public string? Reason { get; set; }

    /// <summary>Administrators only: the user whose subscription is being transitioned.</summary>
    public string? OnBehalfOfUserName { get; set; }

    /// <summary>Resolved from the bearer token; never supplied by the caller.</summary>
    [JsonIgnore]
    public string? UserName { get; set; }
}

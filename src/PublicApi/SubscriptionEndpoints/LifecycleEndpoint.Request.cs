using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : AuthenticatedSubscriptionRequest
{
    /// <summary>The subscription to transition. Taken from the route, not the body.</summary>
    [JsonIgnore]
    public int SubscriptionId { get; set; }

    /// <summary>
    /// True when the caller holds the administrator role, in which case any subscription may be
    /// transitioned. Set from the bearer token, never from the body.
    /// </summary>
    [JsonIgnore]
    public bool IsAdministrator { get; set; }

    /// <summary><c>Pause</c>, <c>Resume</c>, <c>Cancel</c> or <c>Reactivate</c>.</summary>
    public string Action { get; set; }

    /// <summary>
    /// For <c>Cancel</c>: <c>Immediate</c> or <c>EndOfPeriod</c>. Ignored by the other actions.
    /// </summary>
    public string CancellationTiming { get; set; } =
        nameof(ApplicationCore.Entities.SubscriptionAggregate.CancellationTiming.Immediate);

    /// <summary>Optional reason recorded with the transition.</summary>
    public string? Reason { get; set; }
}

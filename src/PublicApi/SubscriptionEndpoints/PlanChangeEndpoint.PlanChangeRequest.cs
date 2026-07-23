using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseRequest
{
    /// <summary>Handle of the plan to move to.</summary>
    public string TargetPlanHandle { get; set; } = string.Empty;

    /// <summary>Whether the change applies immediately with proration, or at the next renewal.</summary>
    public PlanChangeTiming Timing { get; set; } = PlanChangeTiming.Immediate;

    /// <summary>The fingerprint from the preview the caller is confirming. Required on commit.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>Taken from the route, not the body.</summary>
    [JsonIgnore]
    public long SubscriptionId { get; set; }

    /// <summary>Taken from the bearer token, not the body.</summary>
    [JsonIgnore]
    public string? UserName { get; set; }

    /// <summary>Set by the route: the preview route never commits.</summary>
    [JsonIgnore]
    public bool PreviewOnly { get; set; }
}

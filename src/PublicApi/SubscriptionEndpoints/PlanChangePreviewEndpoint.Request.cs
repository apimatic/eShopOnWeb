using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewRequest : AuthenticatedSubscriptionRequest
{
    /// <summary>The subscription to reprice. Taken from the route, not the body.</summary>
    [JsonIgnore]
    public int SubscriptionId { get; set; }

    /// <summary>The plan to move to, e.g. <c>basic-plan</c>.</summary>
    public string TargetPlanHandle { get; set; }

    /// <summary><c>Immediate</c> (prorated) or <c>AtNextRenewal</c> (no proration).</summary>
    public string Timing { get; set; } = nameof(ApplicationCore.Entities.SubscriptionAggregate.PlanChangeTiming.Immediate);
}

using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseRequest
{
    /// <summary>The durable handle of the plan to move to, e.g. "basic-plan".</summary>
    public string TargetPlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// "Immediate" applies now with proration; "AtNextRenewal" schedules the change for the next
    /// period with no proration.
    /// </summary>
    public PlanChangeTiming Timing { get; set; } = PlanChangeTiming.Immediate;

    /// <summary>
    /// The <c>paymentDueInCents</c> that was previewed and shown to the customer. When supplied, the
    /// commit is rejected if the provider no longer quotes that amount, so a change is never applied
    /// at a different price than the one confirmed. Ignored on the preview endpoint.
    /// </summary>
    public int? PreviewedPaymentDueInCents { get; set; }

    /// <summary>Administrators only: the user whose subscription is being changed.</summary>
    public string? OnBehalfOfUserName { get; set; }

    /// <summary>Resolved from the bearer token; never supplied by the caller.</summary>
    [JsonIgnore]
    public string? UserName { get; set; }
}

using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : AuthenticatedSubscriptionRequest
{
    /// <summary>The subscription to move. Taken from the route, not the body.</summary>
    [JsonIgnore]
    public int SubscriptionId { get; set; }

    /// <summary>The plan to move to.</summary>
    public string TargetPlanHandle { get; set; }

    /// <summary><c>Immediate</c> (prorated) or <c>AtNextRenewal</c> (no proration).</summary>
    public string Timing { get; set; } = nameof(ApplicationCore.Entities.SubscriptionAggregate.PlanChangeTiming.Immediate);

    /// <summary>
    /// The <c>paymentDueInCents</c> from the preview the customer confirmed. The change is refused
    /// if the provider now quotes a different amount, so no customer is charged an amount they were
    /// not shown.
    /// </summary>
    public long ConfirmedPaymentDueInCents { get; set; }
}

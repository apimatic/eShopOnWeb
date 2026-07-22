namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseRequest
{
    /// <summary>The subscription to move. Taken from the route.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>The handle of the plan to move to.</summary>
    public string TargetPlanHandle { get; set; }

    /// <summary><c>Immediate</c> (prorated) or <c>AtNextRenewal</c> (no proration). Defaults to immediate.</summary>
    public string Timing { get; set; }

    /// <summary>
    /// The <c>Signature</c> from the preview the customer confirmed. Required when committing;
    /// ignored when previewing.
    /// </summary>
    public string PreviewSignature { get; set; }
}

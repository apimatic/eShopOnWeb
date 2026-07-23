namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseRequest
{
    /// <summary>The plan to move to, identified by its durable handle.</summary>
    public string TargetPlanHandle { get; set; }

    /// <summary><c>Immediate</c> (prorated) or <c>AtNextRenewal</c>. Defaults to immediate.</summary>
    public string Timing { get; set; }

    /// <summary>
    /// The payment due the customer confirmed from the preview. When supplied on an immediate
    /// change, the change is rejected if the cost has moved since.
    /// </summary>
    public decimal? ExpectedPaymentDue { get; set; }

    /// <summary>Administrators only: change the plan for another user.</summary>
    public string UserReference { get; set; }
}

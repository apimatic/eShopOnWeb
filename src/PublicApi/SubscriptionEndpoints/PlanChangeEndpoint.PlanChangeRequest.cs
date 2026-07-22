using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseRequest
{
    public string TargetPlanHandle { get; set; }

    /// <summary>"Immediately" prorates against the current period; "AtNextRenewal" does not.</summary>
    public PlanChangeTiming Timing { get; set; }

    /// <summary>
    /// The quote the caller was shown. When supplied it is re-validated against a fresh preview
    /// and the change is refused if the amounts have moved.
    /// </summary>
    public ConfirmedPreview? ConfirmedPreview { get; set; }

    /// <summary>Set from the route, never from the request body.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>Set from the bearer token; <c>null</c> for administrators.</summary>
    public string? OwnerBuyerId { get; set; }
}

public class ConfirmedPreview
{
    public int ProratedAdjustmentInCents { get; set; }
    public int ChargeInCents { get; set; }
    public int PaymentDueInCents { get; set; }
    public int CreditAppliedInCents { get; set; }
}

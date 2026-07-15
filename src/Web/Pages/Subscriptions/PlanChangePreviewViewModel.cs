namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The previewed cost impact of a plan change, shown to the customer before they confirm (UC3). The
/// staleness token is round-tripped through a hidden form field so the confirm POST can be checked
/// against the subscription's current state before it is applied.
/// </summary>
public class PlanChangePreviewViewModel
{
    public int SubscriptionId { get; set; }
    public string TargetProductHandle { get; set; } = string.Empty;
    public bool ApplyImmediately { get; set; }
    public long? ProratedAdjustmentInCents { get; set; }
    public long? ChargeInCents { get; set; }
    public long? PaymentDueInCents { get; set; }
    public long? CreditAppliedInCents { get; set; }
    public string StalenessToken { get; set; } = string.Empty;
}

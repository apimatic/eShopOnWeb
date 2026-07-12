namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A previewed cost for moving a subscription to a different plan (UC3). The commit call must be
/// given the amounts shown here; the service re-previews at commit time and rejects the commit if
/// they no longer match (never silently applies a different amount than the one previewed).
/// </summary>
public class PlanChangePreview
{
    public int SubscriptionId { get; }
    public string CurrentProductHandle { get; }
    public string TargetProductHandle { get; }
    public int ProratedAdjustmentInCents { get; }
    public int ChargeInCents { get; }
    public int PaymentDueInCents { get; }
    public int CreditAppliedInCents { get; }

    public PlanChangePreview(
        int subscriptionId,
        string currentProductHandle,
        string targetProductHandle,
        int proratedAdjustmentInCents,
        int chargeInCents,
        int paymentDueInCents,
        int creditAppliedInCents)
    {
        SubscriptionId = subscriptionId;
        CurrentProductHandle = currentProductHandle;
        TargetProductHandle = targetProductHandle;
        ProratedAdjustmentInCents = proratedAdjustmentInCents;
        ChargeInCents = chargeInCents;
        PaymentDueInCents = paymentDueInCents;
        CreditAppliedInCents = creditAppliedInCents;
    }

    /// <summary>Value equality on the amounts that matter for staleness detection.</summary>
    public bool HasSameAmounts(PlanChangePreview other) =>
        ProratedAdjustmentInCents == other.ProratedAdjustmentInCents &&
        ChargeInCents == other.ChargeInCents &&
        PaymentDueInCents == other.PaymentDueInCents &&
        CreditAppliedInCents == other.CreditAppliedInCents;
}

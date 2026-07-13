namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The previewed cost of moving a subscription to a different plan. <see cref="ProratedAdjustmentInCents"/>
/// is echoed back on commit so the service can detect a stale preview (§6, Phase 4 / UC3 failure scenarios)
/// without needing any server-side storage of the preview. <see cref="Timing"/> is always
/// <see cref="PlanChangeTiming.Immediate"/> — the Maxio SDK exposes no operation that defers a
/// product migration's commit to the next renewal (confirmed against SDK source; see
/// <see cref="Exceptions.PlanChangeNotSupportedException"/>).
/// </summary>
public class PlanChangePreview
{
    public PlanChangePreview(
        long subscriptionId,
        string fromProductHandle,
        string toProductHandle,
        long proratedAdjustmentInCents,
        long chargeInCents,
        long paymentDueInCents,
        long creditAppliedInCents)
    {
        SubscriptionId = subscriptionId;
        FromProductHandle = fromProductHandle;
        ToProductHandle = toProductHandle;
        ProratedAdjustmentInCents = proratedAdjustmentInCents;
        ChargeInCents = chargeInCents;
        PaymentDueInCents = paymentDueInCents;
        CreditAppliedInCents = creditAppliedInCents;
    }

    public long SubscriptionId { get; }
    public string FromProductHandle { get; }
    public string ToProductHandle { get; }
    public PlanChangeTiming Timing => PlanChangeTiming.Immediate;
    public long ProratedAdjustmentInCents { get; }
    public long ChargeInCents { get; }
    public long PaymentDueInCents { get; }
    public long CreditAppliedInCents { get; }
}

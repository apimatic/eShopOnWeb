using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class PlanChangePreview
{
    public PlanChangePreview(
        int subscriptionId,
        string currentProductHandle,
        string targetProductHandle,
        PlanChangeTiming timing,
        long comparableAmountInCents,
        long? proratedAdjustmentInCents,
        long? chargeInCents,
        long? creditAppliedInCents,
        DateTimeOffset effectiveAt)
    {
        SubscriptionId = subscriptionId;
        CurrentProductHandle = currentProductHandle;
        TargetProductHandle = targetProductHandle;
        Timing = timing;
        ComparableAmountInCents = comparableAmountInCents;
        ProratedAdjustmentInCents = proratedAdjustmentInCents;
        ChargeInCents = chargeInCents;
        CreditAppliedInCents = creditAppliedInCents;
        EffectiveAt = effectiveAt;
    }

    public int SubscriptionId { get; }
    public string CurrentProductHandle { get; }
    public string TargetProductHandle { get; }
    public PlanChangeTiming Timing { get; }

    /// <summary>
    /// The amount re-derived and compared at commit time to detect a stale preview (§UC3).
    /// For <see cref="PlanChangeTiming.Now"/> this is the net prorated adjustment; for
    /// <see cref="PlanChangeTiming.AtRenewal"/> this is the target plan's price.
    /// </summary>
    public long ComparableAmountInCents { get; }
    public long? ProratedAdjustmentInCents { get; }
    public long? ChargeInCents { get; }
    public long? CreditAppliedInCents { get; }
    public DateTimeOffset EffectiveAt { get; }
}

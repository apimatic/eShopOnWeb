namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A previewed proration outcome for a plan change (UC3). Value equality lets the service layer
/// detect a stale preview by re-previewing at commit time and comparing for an exact match.
/// </summary>
public sealed record PlanChangePreview(
    string TargetProductHandle,
    bool ApplyImmediately,
    long ProratedAdjustmentInCents,
    long ChargeInCents,
    long PaymentDueInCents,
    long CreditAppliedInCents);

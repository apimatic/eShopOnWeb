namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Previewed cost of a plan change before it is committed. For an immediate change this is the
/// provider's prorated preview; for a change scheduled at next renewal (no proration applies) this
/// is composed from the target plan's known price, since the provider exposes no preview endpoint
/// for that path.
/// </summary>
public record BillingProrationPreview(
    string TargetProductHandle,
    bool AppliesNow,
    int ProratedAdjustmentInCents,
    int ChargeInCents,
    int PaymentDueInCents,
    int CreditAppliedInCents);

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A preview of a plan change's cost impact. <see cref="StalenessToken"/> captures the subscription's
/// product/version identity at preview time; the billing provider does not expose a formal staleness
/// token (no ETag/version on the migration-preview response), so the commit call must re-fetch the
/// subscription and reject the commit if this token no longer matches (plan.md UC3: "never silently apply
/// a different amount than the one shown").
/// </summary>
public record BillingPlanChangePreview(
    string CurrentProductHandle,
    string TargetProductHandle,
    bool ApplyImmediately,
    long? ProratedAdjustmentInCents,
    long? ChargeInCents,
    long? PaymentDueInCents,
    long? CreditAppliedInCents,
    string StalenessToken);

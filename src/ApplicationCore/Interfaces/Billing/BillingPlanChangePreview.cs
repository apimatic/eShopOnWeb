using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

/// <summary>
/// The previewed cost of moving a subscription to a different plan, shown to the customer
/// before they confirm (see <see cref="IBillingClient.PreviewPlanChangeAsync"/>).
/// </summary>
public sealed record BillingPlanChangePreview
{
    public required string TargetPlanHandle { get; init; }
    public required bool Prorated { get; init; }
    public required DateTimeOffset EffectiveDate { get; init; }

    /// <summary>Net amount due now, in cents. Null when the change is scheduled for renewal (no charge today).</summary>
    public int? ProratedAdjustmentInCents { get; init; }
}

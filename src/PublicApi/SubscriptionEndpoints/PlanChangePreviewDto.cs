namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// What a plan change would cost. All amounts are in minor units (cents); the same values must be
/// echoed back on commit so the change is never applied at a different price (UC3).
/// </summary>
public class PlanChangePreviewDto
{
    public string TargetPlanHandle { get; set; } = string.Empty;
    public string Timing { get; set; } = string.Empty;
    public long ProratedAdjustmentInCents { get; set; }
    public long ChargeInCents { get; set; }
    public long PaymentDueInCents { get; set; }
    public long CreditAppliedInCents { get; set; }

    /// <summary><see cref="PaymentDueInCents"/> in major units (dollars).</summary>
    public decimal PaymentDue { get; set; }
}

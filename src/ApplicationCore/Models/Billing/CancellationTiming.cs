namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// When a cancellation takes effect (UC4).
/// </summary>
public enum CancellationTiming
{
    /// <summary>Cancel straight away; the subscription stops accruing charges now.</summary>
    Immediate,

    /// <summary>Defer the cancellation to the end of the current billing period.</summary>
    EndOfPeriod
}

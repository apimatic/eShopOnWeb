namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// When a plan change should take effect.
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>Apply the change straight away and prorate the difference.</summary>
    Immediate = 0,

    /// <summary>Schedule the change for the next renewal; no proration applies.</summary>
    AtNextRenewal = 1
}

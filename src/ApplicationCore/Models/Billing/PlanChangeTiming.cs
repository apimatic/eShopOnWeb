namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// When a plan change takes effect, and therefore whether it is prorated (UC3).
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>Apply now: the billing period is preserved and a prorated charge or credit is issued.</summary>
    ImmediateWithProration,

    /// <summary>Apply at the next renewal: the billing period resets and the full new price is charged.</summary>
    AtNextRenewal
}

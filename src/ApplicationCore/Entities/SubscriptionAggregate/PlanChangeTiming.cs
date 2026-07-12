namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The two commit timings UC3 offers the customer for a plan change.
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>Apply now; the prorated charge/credit previewed is billed immediately.</summary>
    Immediate,

    /// <summary>Apply at the next renewal; no proration is charged.</summary>
    AtNextRenewal
}

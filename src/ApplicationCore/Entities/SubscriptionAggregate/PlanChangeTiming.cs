namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a plan change takes effect (UC3).
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>Apply the change immediately, prorating the remainder of the current period.</summary>
    Immediate = 0,

    /// <summary>Defer the change to the next renewal; no proration is applied.</summary>
    AtNextRenewal
}

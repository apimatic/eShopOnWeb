namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a plan change takes effect (UC3).
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>Apply the change immediately, prorating the current period.</summary>
    Immediately = 0,

    /// <summary>Schedule the change for the next renewal; no proration is applied.</summary>
    AtNextRenewal = 1
}

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a plan change takes effect (UC3).
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>Apply immediately; the provider prorates the remainder of the current period.</summary>
    Immediately = 0,

    /// <summary>Schedule for the next renewal date; no proration applies.</summary>
    AtNextRenewal
}

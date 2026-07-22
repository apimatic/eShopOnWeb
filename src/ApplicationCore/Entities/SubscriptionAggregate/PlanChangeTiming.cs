namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a plan change takes effect (UC3).
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>
    /// Apply straight away, keeping the current billing period and prorating the difference.
    /// </summary>
    Immediately = 0,

    /// <summary>
    /// Apply at the next renewal, with no proration.
    /// </summary>
    AtNextRenewal
}

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a plan change takes effect (UC3).
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>Apply the change now and prorate the remainder of the current period.</summary>
    Immediate,

    /// <summary>Apply the change at the next renewal; no proration is charged or credited.</summary>
    NextRenewal
}

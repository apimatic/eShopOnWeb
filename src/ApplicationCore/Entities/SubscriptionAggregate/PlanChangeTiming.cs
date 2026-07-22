namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a plan change takes effect.
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>Apply straight away and prorate the current period.</summary>
    Immediately = 0,

    /// <summary>Defer to the next renewal; the current period is untouched and nothing is prorated.</summary>
    AtNextRenewal
}

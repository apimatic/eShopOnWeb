namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a plan change (UC3) takes effect.
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>Apply straight away and prorate the remainder of the current period.</summary>
    Immediate = 0,

    /// <summary>Defer to the start of the next billing period; nothing is prorated.</summary>
    AtNextRenewal = 1
}

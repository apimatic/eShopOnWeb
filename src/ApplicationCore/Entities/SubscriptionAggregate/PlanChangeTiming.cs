namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a plan change takes effect, which also decides whether it is prorated.
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>Apply now, keeping the billing period, so the provider issues a prorated charge or credit.</summary>
    Immediately = 0,

    /// <summary>Schedule the change for the next renewal, so nothing is prorated.</summary>
    AtNextRenewal
}

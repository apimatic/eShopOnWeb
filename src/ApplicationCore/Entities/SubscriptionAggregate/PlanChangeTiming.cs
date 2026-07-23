namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a UC3 plan change takes effect.
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>Apply the change straight away; the provider prorates the current period.</summary>
    Immediate = 0,

    /// <summary>Defer the change to the start of the next billing period; nothing is prorated.</summary>
    AtNextRenewal = 1
}

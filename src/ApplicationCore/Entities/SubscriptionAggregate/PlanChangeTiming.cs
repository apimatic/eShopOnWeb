namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>When a plan change takes effect, which also decides whether it is prorated.</summary>
public enum PlanChangeTiming
{
    /// <summary>Apply now, keeping the current billing period and issuing a prorated charge or credit.</summary>
    Immediate = 0,

    /// <summary>Apply at the next renewal; the billing period restarts and nothing is prorated.</summary>
    AtNextRenewal = 1
}

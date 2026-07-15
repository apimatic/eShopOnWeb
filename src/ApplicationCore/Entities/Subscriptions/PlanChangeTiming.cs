namespace Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;

public enum PlanChangeTiming
{
    // Apply immediately with a prorated charge/credit.
    Now,

    // Apply automatically at the subscription's next renewal date; no proration.
    AtNextRenewal
}

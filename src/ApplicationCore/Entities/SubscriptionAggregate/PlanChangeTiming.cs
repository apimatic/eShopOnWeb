namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>When a plan change (UC3) takes effect.</summary>
public enum PlanChangeTiming
{
    /// <summary>Apply the change straight away; the billing period is preserved and the change is prorated.</summary>
    Immediately = 0,

    /// <summary>Schedule the change for the next renewal; no proration is applied.</summary>
    AtNextRenewal = 1
}

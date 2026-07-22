namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>When a plan change takes effect.</summary>
public enum PlanChangeTiming
{
    /// <summary>Apply straight away and prorate the difference.</summary>
    Immediate = 0,

    /// <summary>Queue the change for the next renewal; no proration applies.</summary>
    NextRenewal = 1
}

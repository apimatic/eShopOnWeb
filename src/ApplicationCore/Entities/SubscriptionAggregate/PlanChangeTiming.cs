namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a plan change takes effect, and therefore whether the customer is prorated.
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>Apply the change straight away and prorate the current period.</summary>
    Immediate = 0,

    /// <summary>Schedule the change for the next renewal; no proration is charged.</summary>
    AtNextRenewal
}

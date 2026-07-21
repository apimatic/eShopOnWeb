namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a plan change should take effect.
/// </summary>
public enum PlanChangeTiming
{
    /// <summary>Apply now, with a prorated charge or credit.</summary>
    Now,

    /// <summary>Apply at the next renewal, with no proration.</summary>
    AtRenewal,
}

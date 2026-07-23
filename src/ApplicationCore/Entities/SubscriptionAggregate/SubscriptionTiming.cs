namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>When a plan change takes effect.</summary>
public enum PlanChangeTiming
{
    /// <summary>Apply now, prorating the remainder of the current period.</summary>
    Immediate = 0,

    /// <summary>Apply at the next renewal. No proration applies.</summary>
    AtNextRenewal = 1
}

/// <summary>When a cancellation takes effect.</summary>
public enum CancellationTiming
{
    /// <summary>Cancel now.</summary>
    Immediate = 0,

    /// <summary>Cancel when the current billing period ends.</summary>
    EndOfBillingPeriod = 1
}

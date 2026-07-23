namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The lifecycle transitions a customer or admin can request on an existing subscription (plan.md UC4).
/// </summary>
public enum SubscriptionLifecycleAction
{
    /// <summary>Place an active subscription on hold.</summary>
    Pause = 0,

    /// <summary>Take a held subscription back to active.</summary>
    Resume = 1,

    /// <summary>Cancel with immediate effect.</summary>
    CancelImmediately = 2,

    /// <summary>Schedule the cancellation for the end of the current billing period.</summary>
    CancelAtEndOfPeriod = 3,

    /// <summary>Bring a cancelled subscription back to active.</summary>
    Reactivate = 4
}

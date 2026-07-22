namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic lifecycle state of a subscription. The billing provider remains the system of
/// record; this enumeration is the normalized view eShopOnWeb reasons about.
/// </summary>
public enum SubscriptionLifecycleState
{
    /// <summary>The provider reported a state this build does not model.</summary>
    Unknown = 0,
    Pending = 1,
    Trialing = 2,
    Active = 3,
    Paused = 4,
    PastDue = 5,
    Suspended = 6,
    Canceled = 7,
    Expired = 8,
    TrialEnded = 9,
    Unpaid = 10,
    Failed = 11
}

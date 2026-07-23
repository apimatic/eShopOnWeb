namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic lifecycle state of a subscription.
/// The billing provider is the system of record; this enum is the normalized view of its state.
/// </summary>
public enum SubscriptionState
{
    /// <summary>The provider reported a state this application does not model.</summary>
    Unknown = 0,
    Pending,
    AwaitingSignup,
    Trialing,
    TrialEnded,
    Assessing,
    Active,
    SoftFailure,
    PastDue,
    Suspended,
    Paused,
    Unpaid,
    Canceled,
    Expired,
    FailedToCreate
}

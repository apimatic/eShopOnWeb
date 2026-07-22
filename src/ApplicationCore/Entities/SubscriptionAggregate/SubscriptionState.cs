namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic lifecycle state of a subscription. The billing provider is the system of
/// record for this value; <see cref="Unknown"/> represents a state the provider reported that
/// this application does not model, and is never treated as a state a transition may start from.
/// </summary>
public enum SubscriptionState
{
    Unknown = 0,
    Pending,
    AwaitingSignup,
    Trialing,
    Assessing,
    Active,
    SoftFailure,
    PastDue,
    Suspended,
    Canceled,
    Expired,
    Paused,
    Unpaid,
    TrialEnded,
    OnHold,
    FailedToCreate
}

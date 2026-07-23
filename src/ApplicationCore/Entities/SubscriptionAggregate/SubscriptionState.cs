namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic lifecycle state of a subscription.
/// </summary>
/// <remarks>
/// <see cref="Unknown"/> is deliberately present: the billing provider is free to introduce
/// new states, and an unrecognized state must never crash a read path. Callers treat
/// <see cref="Unknown"/> as "not safe to transition" rather than guessing.
/// </remarks>
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
    OnHold,
    Unpaid,
    TrialEnded,
    FailedToCreate
}

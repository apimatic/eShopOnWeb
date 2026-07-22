namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic lifecycle state of a subscription.
/// </summary>
public enum SubscriptionState
{
    Unknown = 0,
    Pending,
    AwaitingSignup,
    FailedToCreate,
    Trialing,
    TrialEnded,
    Assessing,
    Active,
    SoftFailure,
    PastDue,
    Suspended,
    Paused,
    OnHold,
    Unpaid,
    Canceled,
    Expired
}

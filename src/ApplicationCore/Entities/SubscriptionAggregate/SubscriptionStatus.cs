namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic lifecycle state of a subscription. The billing provider is the system of
/// record; <see cref="Unknown"/> is used when the provider reports a state this application does
/// not model, so that an unrecognised value never crashes a read path.
/// </summary>
public enum SubscriptionStatus
{
    Unknown = 0,
    Pending,
    Trialing,
    TrialEnded,
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
    FailedToCreate,
    AwaitingSignup
}

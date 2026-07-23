namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The lifecycle state of a subscription held by the billing provider.
/// </summary>
public enum SubscriptionState
{
    Unknown = 0,
    Pending,
    FailedToCreate,
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
    AwaitingSignup
}

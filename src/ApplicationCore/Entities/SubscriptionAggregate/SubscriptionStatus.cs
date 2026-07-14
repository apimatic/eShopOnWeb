namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum SubscriptionStatus
{
    Pending,
    AwaitingSignup,
    Trialing,
    TrialEnded,
    Assessing,
    Active,
    SoftFailure,
    PastDue,
    Unpaid,
    Suspended,
    OnHold,
    Paused,
    Canceled,
    Expired,
    FailedToCreate,
    Unknown
}

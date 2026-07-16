namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Domain-owned mirror of the billing provider's subscription lifecycle state.
/// Kept independent of the provider SDK so ApplicationCore never references it directly.
/// </summary>
public enum SubscriptionState
{
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
    AwaitingSignup,
    Other
}

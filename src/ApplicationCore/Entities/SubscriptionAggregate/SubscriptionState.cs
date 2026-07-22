namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The lifecycle state of a subscription, expressed in provider-agnostic terms.
/// The billing client is responsible for mapping the provider's vocabulary onto these values.
/// </summary>
public enum SubscriptionState
{
    Unknown = 0,
    Pending,
    AwaitingSignup,
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
    OnHold
}

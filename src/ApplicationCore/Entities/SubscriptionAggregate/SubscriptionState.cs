namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic lifecycle state of a subscription. Maps 1:1 onto the billing
/// provider's own state values so ApplicationCore never needs to reference the provider SDK.
/// </summary>
public enum SubscriptionState
{
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
    Canceled,
    Expired,
    Paused,
    OnHold,
    Unpaid,
    FailedToCreate
}

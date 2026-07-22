namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic lifecycle state of a subscription. Billing clients map their
/// provider's own vocabulary onto these values.
/// </summary>
public enum SubscriptionState
{
    Unknown = 0,
    Pending,
    Trialing,
    Active,
    Assessing,
    SoftFailure,
    PastDue,
    Suspended,
    Paused,
    Unpaid,
    TrialEnded,
    AwaitingSignup,
    Canceled,
    Expired,
    FailedToCreate
}

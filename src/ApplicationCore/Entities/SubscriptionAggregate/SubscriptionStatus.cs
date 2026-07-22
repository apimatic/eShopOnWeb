namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic lifecycle state of a subscription. Unrecognised provider states map to
/// <see cref="Unknown"/> so a newly introduced provider state can never crash the storefront.
/// </summary>
public enum SubscriptionStatus
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
    Unpaid,
    Paused,
    Canceled,
    Expired,
    FailedToCreate
}

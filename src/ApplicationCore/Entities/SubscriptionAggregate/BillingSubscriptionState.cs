namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic subscription lifecycle state. Maps 1:1 onto Maxio's subscription states, with
/// its "on hold" and "paused" states merged into a single <see cref="Paused"/> value since which one
/// a given pause call actually produces is not guaranteed by the provider's contract.
/// </summary>
public enum BillingSubscriptionState
{
    Unknown = 0,
    Pending,
    AwaitingSignup,
    Trialing,
    Active,
    Assessing,
    SoftFailure,
    PastDue,
    Suspended,
    Paused,
    Unpaid,
    TrialEnded,
    Canceled,
    Expired,
    FailedToCreate,
}

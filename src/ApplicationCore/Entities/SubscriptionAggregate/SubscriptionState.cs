namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic lifecycle state of a subscription.
/// </summary>
public enum SubscriptionState
{
    /// <summary>The provider reported a state this integration does not model.</summary>
    Unknown = 0,
    Trialing,
    Active,
    Paused,
    PastDue,
    SoftFailure,
    Unpaid,
    Canceled,
    Expired,
    Failed,
    Suspended,
    Pending
}

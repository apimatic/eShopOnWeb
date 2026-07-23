namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic lifecycle state of a subscription. The billing provider remains the
/// system of record; this enum is the normalized view eShopOnWeb reasons about.
/// </summary>
public enum SubscriptionState
{
    /// <summary>The provider reported a state this integration does not model.</summary>
    Unknown = 0,
    Pending,
    Trialing,
    Active,
    PastDue,
    Suspended,
    Paused,
    Canceled,
    Expired,
    Unpaid,
    TrialEnded,
    Failed
}

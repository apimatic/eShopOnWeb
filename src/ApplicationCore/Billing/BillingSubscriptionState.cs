namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Provider-agnostic subscription lifecycle state.
/// </summary>
public enum BillingSubscriptionState
{
    /// <summary>The provider reported a state this application does not model.</summary>
    Unknown = 0,
    Pending,
    Trialing,
    Active,
    PastDue,
    Paused,
    Canceled,
    Expired,
    Unpaid,
    Suspended,
    TrialEnded,
    Failed
}

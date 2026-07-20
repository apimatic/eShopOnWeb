namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

/// <summary>
/// Provider-agnostic projection of the billing subscription's lifecycle state.
/// </summary>
public enum SubscriptionLifecycleState
{
    Pending,
    Trialing,
    Active,
    PastDue,
    Paused,
    Canceled,
    Expired,
    Unpaid,
    Other
}

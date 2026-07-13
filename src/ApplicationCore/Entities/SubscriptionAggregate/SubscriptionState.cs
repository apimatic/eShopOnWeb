namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic lifecycle state of a subscription. The billing client maps its own
/// vocabulary onto this enum so ApplicationCore never depends on provider-specific state names.
/// </summary>
public enum SubscriptionState
{
    Unknown,
    Active,
    Trialing,
    OnHold,
    PastDue,
    Unpaid,
    TrialEnded,
    Canceled
}

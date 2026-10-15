namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Lifecycle of a claim taken before a write to the billing provider.
/// </summary>
public enum BillingClaimStatus
{
    /// <summary>The claim is held and the provider write is in flight.</summary>
    Pending = 0,

    /// <summary>The provider confirmed the write; the provider id is recorded.</summary>
    Completed = 1,

    /// <summary>The provider may or may not have acted (connection lost, timeout, 5xx); must be settled before reuse.</summary>
    Unknown = 2
}

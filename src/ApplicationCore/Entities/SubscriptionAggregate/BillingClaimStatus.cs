namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Lifecycle of a local claim guarding a write to the billing provider.
/// </summary>
public enum BillingClaimStatus
{
    /// <summary>A request owns the claim and is talking to the billing provider.</summary>
    Pending = 0,

    /// <summary>The provider write is confirmed and its id is recorded.</summary>
    Completed = 1,

    /// <summary>A provider write may or may not have landed; the next request for the same key settles it first.</summary>
    Unknown = 2
}
